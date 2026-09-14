using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        [Serializable] private sealed class GripADependency { public string path, sha256; }
        [Serializable] private sealed class GripAEvidenceStamp
        {
            public string utc, command, bodyModel, gestureProfile, appearanceProfile, controller, motorProfile;
            public GripADependency[] dependencies;
            public string scope = "Asset/code identity at this scoped hand diagnostic. No native input, whole-cloth or complete gameplay acceptance.";
        }
        [Serializable] private sealed class GripAPreservationGate
        {
            public string status, source_fbx_sha256;
            public int bone_count;
            public float outside_right_hand_max_change_source_units, bone_bind_matrix_max_change;
        }
        [Serializable] private sealed class GripASyncReport
        {
            public string status, prefab, appearanceProfile, controller, motorProfile, acceptance;
            public bool sceneActorUnchanged, motorRemainsOnPlayerParent;
        }

        private static GripAEvidenceStamp ReadGripAIdentity(WorldMacroPlayerGestureRig rig, string command)
        {
            var appearance = rig.GetComponent<WorldMacroPlayerAppearance>();
            var motor = rig.GetComponentInParent<PlayerMotor>();
            var animator = rig.GetComponent<Animator>();
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            Action<string> add = p => { if (string.IsNullOrEmpty(p)) return; if (File.Exists(p)) paths.Add(p); if (File.Exists(p + ".meta")) paths.Add(p + ".meta"); };
            foreach (var skin in ReadBoundSkinSources(rig)) add(skin.assetPath);
            add(AssetDatabase.GetAssetPath(rig.Profile));
            if (appearance != null) add(AssetDatabase.GetAssetPath(appearance.Profile));
            if (motor != null) add(AssetDatabase.GetAssetPath(motor.LocomotionProfile));
            if (animator != null)
            {
                add(AssetDatabase.GetAssetPath(animator.avatar)); add(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController));
                if (animator.runtimeAnimatorController != null)
                    foreach (var clip in animator.runtimeAnimatorController.animationClips) add(AssetDatabase.GetAssetPath(clip));
            }
            foreach (string file in new[] {
                "App/World/WorldMacroPlayerGestureRig.cs", "App/World/WorldMacroPlayerGestureProfile.cs", "App/World/WorldMacroPlayerAppearance.cs",
                "App/World/WorldMacroPlayerAppearanceProfile.cs", "App/World/WorldMacroPlayerFootPlacement.cs",
                "Combat/Player/PlayerMotor.cs", "Combat/Player/PlayerMotor.Locomotion.cs", "Combat/Player/PlayerLocomotionProfileSO.cs",
                "Presentation/BrushBristleRig.cs", "Presentation/PlayerVisualIK.cs" }) add("Assets/_Project/Scripts/" + file);
            foreach (var component in rig.GetComponentsInChildren<Oheangbu.Presentation.BrushBristleRig>(true))
            {
                var data = new SerializedObject(component); var profile = data.FindProperty("_profile");
                if (profile != null && profile.propertyType == SerializedPropertyType.ObjectReference) add(AssetDatabase.GetAssetPath(profile.objectReferenceValue));
            }
            return new GripAEvidenceStamp {
                utc = DateTime.UtcNow.ToString("o"), command = command, bodyModel = ActiveSkinSourcePath(rig), gestureProfile = AssetDatabase.GetAssetPath(rig.Profile),
                appearanceProfile = appearance != null ? AssetDatabase.GetAssetPath(appearance.Profile) : "", controller = animator != null ? AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) : "",
                motorProfile = motor != null ? AssetDatabase.GetAssetPath(motor.LocomotionProfile) : "",
                dependencies = paths.Select(p => new GripADependency { path = p, sha256 = Sha(p) }).ToArray()
            };
        }

        private static void WriteGripAEvidenceStamp(WorldMacroPlayerGestureRig rig, string command)
        {
            string folder = Path.Combine(GripASource, "Validation/Unity"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "evidence_stamp_" + command + ".json"), JsonUtility.ToJson(ReadGripAIdentity(rig, command), true));
        }

        private static void RequireGripAEvidenceIdentity(WorldMacroPlayerGestureRig rig, string command)
        {
            string path = Path.Combine(GripASource, "Validation/Unity/evidence_stamp_" + command + ".json");
            Need(File.Exists(path), "Run the source-specific A " + command + " diagnostic after applying the final locomotion assets.");
            var saved = JsonUtility.FromJson<GripAEvidenceStamp>(File.ReadAllText(path)); var current = ReadGripAIdentity(rig, command);
            Need(saved != null && saved.bodyModel == current.bodyModel && saved.gestureProfile == current.gestureProfile && saved.appearanceProfile == current.appearanceProfile
                && saved.controller == current.controller && saved.motorProfile == current.motorProfile, "The A " + command + " diagnostic belongs to another hand/movement configuration.");
            Need(saved.dependencies != null && saved.dependencies.Length == current.dependencies.Length && saved.dependencies.Zip(current.dependencies,
                (a, b) => a.path == b.path && a.sha256 == b.sha256).All(equal => equal),
                "A " + command + " evidence is stale: current mesh, importer, profile, controller, clips, or presentation/movement code changed.");
        }

        public static bool HasGripAReleaseGate(WorldMacroPlayerAppearance appearance, out string detail)
        {
            try
            {
                Need(appearance != null && appearance.Animator != null, "Current appearance/Animator missing.");
                var rig = appearance.GetComponent<WorldMacroPlayerGestureRig>(); var motor = appearance.GetComponentInParent<PlayerMotor>();
                string motionFolder = _recoveryHand ? WorldMacroLocomotionAuthoring.RecoveryFolder : WorldMacroLocomotionAuthoring.OriginalFolder;
                Need(rig != null && ActiveSkinSourcePath(rig) == GripAModel && AssetDatabase.GetAssetPath(rig.Profile) == GripAProfile, "Current actor is not the actual A mesh/profile combination.");
                Need(AssetDatabase.GetAssetPath(appearance.Profile) == motionFolder+"/PlayerAppearance_Locomotion.asset" && appearance.Profile.DirectionalLocomotion
                    && appearance.Animator.runtimeAnimatorController == appearance.Profile.Controller && AssetDatabase.GetAssetPath(appearance.Profile.Controller) == motionFolder+"/AC_PlaytestLocomotion.controller",
                    "Expected current directional locomotion profile/controller, not the old movement setup.");
                Need(motor != null && AssetDatabase.GetAssetPath(motor.LocomotionProfile) == motionFolder+"/PlayerLocomotion.asset",
                    "Expected the installed opt-in locomotion motor profile on the player parent.");
                Need(appearance.Animator.avatar != null && appearance.Animator.avatar.isValid && appearance.Animator.avatar.isHuman && !appearance.Animator.applyRootMotion,
                    "The active fixed skeleton must remain a valid Humanoid with root motion off.");
                var clips = appearance.Profile.DirectionalWalk.Concat(appearance.Profile.DirectionalRun).Concat(appearance.Profile.DirectionalDodge)
                    .Concat(new[] { appearance.Profile.SitDown, appearance.Profile.SitIdle, appearance.Profile.StandUp, appearance.Profile.JumpRise, appearance.Profile.JumpFall, appearance.Profile.Land }).ToArray();
                Need(clips.Length == 18 && clips.All(c => c != null && c.humanMotion && c.length > 0f), "All eighteen current Humanoid locomotion actions are required.");
                Need(rig.Profile.Fingers != null && rig.Profile.Fingers.Length == 30 && rig.Profile.Fingers.All(f => f != null && f.UseExplicitRestPose
                    && WorldMacroPlayerGestureProfile.IsUsableRotation(f.RestLocalRotation) && WorldMacroPlayerGestureProfile.IsUsableRotation(f.CarryOffset)), "A calibrated finger bindings are incomplete.");
                string sha = Sha(GripAModel); Need(sha == Sha(Path.Combine(GripASource, "Exports/Player_C02_GripA.fbx")), "Imported A FBX differs from its source export.");
                var surface = JsonUtility.FromJson<GripASurface>(File.ReadAllText(Path.Combine(GripASource, "Validation/hand_surface_sampling.json")));
                Need(surface.status == "PASS_SAMPLED_STATIC_SURFACE" && surface.sourceFbxSha256 == sha && surface.samples > 0
                    && surface.maximum_sampled_penetration_m <= .0005f && surface.ray_misses == 0, "A static triangle-surface gate is missing/stale/failed.");
                if (_recoveryHand)
                {
                    var preserved=JsonUtility.FromJson<RecoveryRoundtrip>(File.ReadAllText(Path.Combine(GripASource,"Validation/fbx_roundtrip.json")));
                    Need(preserved.status=="PASS"&&preserved.fbxSha256==sha&&preserved.bones==54&&preserved.sourceBindUnchanged&&preserved.outsideHandCoordinatesUnchanged&&preserved.maxWeightCount<=4&&preserved.maxWeightSumError<=.0001f,"Recovery mesh/bind/4-weight roundtrip failed or stale.");
                    Need(appearance.Profile.RecoveryMotions&&appearance.Profile.DirectionalCrouch.Length==4&&appearance.Profile.DirectionalCrouch.Concat(new[]{appearance.Profile.CrouchIdle,appearance.Profile.StartForward,appearance.Profile.TurnLeft,appearance.Profile.TurnRight}).All(c=>c!=null&&c.humanMotion&&c.length>0),"Recovery source motions incomplete.");
                    var feet=JsonUtility.FromJson<RecoveryFootGate>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/Motion/Unity/unity_synthetic_feet.json"))));
                    Need(feet.status=="SYNTHETIC_FOOT_LIMITS_PASS_VISUAL_UNVERIFIED"&&feet.modelSha256==sha&&feet.controllerSha256==Sha(motionFolder+"/AC_PlaytestLocomotion.controller")&&feet.actionsSha256==Sha(motionFolder+"/Player_Locomotion_Actions.fbx")&&feet.supportSha256==Sha(motionFolder+"/AuthoredMotionSupport.json")&&feet.solverSha256==Sha("Assets/_Project/Scripts/App/World/WorldMacroPlayerFootPlacement.cs"),"Recovery foot checks failed or stale.");
                }
                else
                {
                    var preserved = JsonUtility.FromJson<GripAPreservationGate>(File.ReadAllText(Path.Combine(GripASource, "Validation/preservation.json")));
                    Need(preserved.status == "PASS_PRESERVATION" && preserved.source_fbx_sha256 == sha && preserved.bone_count == 54
                        && preserved.outside_right_hand_max_change_source_units == 0f && preserved.bone_bind_matrix_max_change == 0f, "A fixed-bind/body preservation gate failed.");
                }
                string evidence = Path.Combine(GripASource, "Validation/Unity");
                var presentation = JsonUtility.FromJson<GestureQaReport>(File.ReadAllText(Path.Combine(evidence, "runtime_gesture_qa.json")));
                Need(presentation.status == "PASS_SYNTHETIC_PRESENTATION_ONLY" && presentation.sourceFbxSha256 == sha && presentation.passed >= 50 && presentation.failed == 0
                    && presentation.restorationComplete && presentation.rawStrokeStatePreserved && presentation.noGameplayDrawingEvents && presentation.gameplayCameraPreserved,
                    "The current A center/edge/pause/recovery presentation diagnostic has not passed.");
                var contact = JsonUtility.FromJson<ContactReport>(File.ReadAllText(Path.Combine(evidence, "current_skin_contact.json")));
                Need(contact.status == "PASS_CURRENT_POSE_VERTEX_CONTACT_ONLY" && contact.sourceFbxSha256 == sha && contact.maximumPenetrationMeters <= .0005f
                    && contact.bakeConventionValidated && contact.playerTransformsPreserved && contact.qualityRestored && contact.temporaryMeshesReleased,
                    "The actual current A skin/shaft contact diagnostic has not passed.");
                RequireGripAEvidenceIdentity(rig, "check"); RequireGripAEvidenceIdentity(rig, "contact");
                detail = "Exact current A mesh/importer/profile + locomotion/controller/18 clips/code fingerprints match their A static surface, live vertex-contact and synthetic presentation reports. Native input, terrain, cloth and final art approval remain unverified; no old RIG_PASS substituted.";
                return true;
            }
            catch (Exception exception) { detail = exception.Message; return false; }
        }
        [Serializable] class RecoveryRoundtrip {public string status,fbxSha256;public int bones,maxWeightCount;public float maxWeightSumError;public bool sourceBindUnchanged,outsideHandCoordinatesUnchanged;}
        [Serializable] class RecoveryFootGate {public string status,modelSha256,controllerSha256,actionsSha256,supportSha256,solverSha256;}
        public static bool HasRecoveryReleaseGate(WorldMacroPlayerAppearance appearance,out string detail)
        {bool old=_recoveryHand;_recoveryHand=true;try{return HasGripAReleaseGate(appearance,out detail);}finally{_recoveryHand=old;}}

        private static string SyncGripAVisualPrefab()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode, "Visual prefab sync requires Edit mode.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); Need(scene.path == WorldMacroPlaytestAuthoring.ScenePath, "Open the existing saved playtest scene.");
            var matches = Object.FindObjectsByType<WorldMacroPlayerAppearance>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(a => a.gameObject.scene == scene && a.GetComponent<WorldMacroPlayerGestureRig>() != null).ToArray();
            Need(matches.Length == 1 && HasGripAReleaseGate(matches[0], out _), "Current combined A + locomotion scoped acceptance must pass before prefab synchronization.");
            var current = matches[0]; HasGripAReleaseGate(current, out string detail);
            var prefab = PrefabUtility.LoadPrefabContents(GripAPrefab);
            try
            {
                var appearance = prefab.GetComponent<WorldMacroPlayerAppearance>(); var gesture = prefab.GetComponent<WorldMacroPlayerGestureRig>(); var animator = prefab.GetComponent<Animator>();
                Need(appearance != null && animator != null && gesture != null && ActiveSkinSourcePath(gesture) == GripAModel && AssetDatabase.GetAssetPath(gesture.Profile) == GripAProfile,
                    "The A prefab body/gesture must already match; sync never replaces models or actors.");
                var data = new SerializedObject(appearance); data.FindProperty("_profile").objectReferenceValue = current.Profile; data.ApplyModifiedPropertiesWithoutUndo();
                animator.runtimeAnimatorController = current.Animator.runtimeAnimatorController; animator.avatar = current.Animator.avatar; animator.applyRootMotion = false;
                EditorUtility.SetDirty(appearance); EditorUtility.SetDirty(animator);
                Need(PrefabUtility.SaveAsPrefabAsset(prefab, GripAPrefab) != null, "Could not save the existing A visual prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            var report = new GripASyncReport { status = "SYNCED_A_VISUAL_PREFAB_LOCOMOTION_REFERENCES", prefab = GripAPrefab, appearanceProfile = AssetDatabase.GetAssetPath(current.Profile),
                controller = AssetDatabase.GetAssetPath(current.Animator.runtimeAnimatorController), motorProfile = WorldMacroLocomotionAuthoring.MotorProfilePath,
                sceneActorUnchanged = true, motorRemainsOnPlayerParent = true, acceptance = detail };
            string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(GripASource, "Validation/prefab_sync.json"), json); return json;
        }
    }
}
