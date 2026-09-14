using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Creates and installs only the playtest-owned C02 appearance derivative.</summary>
    public static partial class WorldMacroPlayerAppearanceAuthoring
    {
        public const string Folder = WorldMacroPlaytestAuthoring.Folder + "/PlayerAppearance";
        public const string SourceModelPath = "Assets/_Project/Art/C02_RigFaceLab/Models/Integrated_B2_C3.fbx";
        public const string IdlePath = "Assets/_Project/Art/C02_RigFaceLab/Animations/Integrated_B2_C3_Idle.anim";
        public const string WalkPath = "Assets/_Project/Art/C02_RigFaceLab/Animations/Integrated_B2_C3_Walk.anim";
        public const string RunPath = "Assets/_Project/Art/C02_RigFaceLab/Animations/Integrated_B2_C3_Run.anim";
        public const string ControllerPath = Folder + "/AC_C02_A2B2C3_Playtest.controller";
        public const string ProfilePath = Folder + "/PlayerAppearance_C02_A2B2C3.asset";
        public const string PrefabPath = Folder + "/PF_C02_A2B2C3_Playtest.prefab";
        public const string ProfileScriptPath = "Assets/_Project/Scripts/App/World/WorldMacroPlayerAppearanceProfile.cs";
        public const string SceneRootName = "Playtest_PlayerAppearance_C02_A2B2C3";
        public const int ExpectedTriangles = 52784;

        private static readonly string[] SourceMaterialPaths =
        {
            "Assets/_Project/Art/C02_RigFaceLab/Materials/Integrated_B2_C3_M0.mat",
            "Assets/_Project/Art/C02_RigFaceLab/Materials/Integrated_B2_C3_M1.mat"
        };

        public static readonly string[] OwnedMaterialPaths =
        {
            Folder + "/Materials/M_C02_A2B2C3_0_Playtest.mat",
            Folder + "/Materials/M_C02_A2B2C3_1_Playtest.mat"
        };

        private const float TargetHeight = 1.75f;
        private const float NativeWalkSpeed = 1.5537528f;
        private const float NativeRunSpeed = 5.362285f;
        private const float GameRunSpeed = 4.5f;

        [Serializable]
        private sealed class Report
        {
            public string status;
            public string scope = "Playtest-owned C02 appearance only; art quality and user-controlled route play remain separate acceptance.";
            public string command;
            public string scene;
            public string model;
            public string prefab;
            public string controller;
            public string profile;
            public bool sourceModelFound;
            public bool avatarValid;
            public bool avatarHuman;
            public bool clipsFound;
            public bool clipsHuman;
            public bool rootMotionOff;
            public bool speedParameterLinked;
            public bool profileScriptBound;
            public bool ownedMaterialPaths;
            public bool ownedTexturesMatchSources;
            public bool installed;
            public bool primitiveRendererDisabled;
            public bool cameraRendererContract;
            public bool walkerRendererContract;
            public bool noCloth;
            public bool requiredBones;
            public bool materialsPreserved;
            public bool texturesPresent;
            public bool colliderMatches;
            public bool heightMatches;
            public bool footPlaneMatches;
            public bool triangleCountMatches;
            public bool fingerBonesPresent;
            public int triangles;
            public int renderers;
            public int materials;
            public int blinkChannels;
            public float renderedHeight;
            public float footPlane;
            public float uniformScale;
            public float ambientFloor;
            public string[] playerMaterials = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
            public string[] limitations =
            {
                "B2 supplies forward Idle/Walk/Run only; lateral movement reuses forward gait.",
                "The selected Humanoid has no finger bones, so the drawing adapter aims shoulder/forearm/hand but does not form a brush grip.",
                "A2 clothing penetration, C3 eyelid art quality, route foot contact, shadows, and performance still require current-scene visual review.",
                "No Cloth, secondary-motion rig, lab controller, lab camera, or lab skinning scope is installed."
            };
        }

        private struct Geometry
        {
            public Bounds Bounds;
            public int Triangles;
            public int Renderers;
            public int Materials;
            public int BlinkChannels;
            public bool MaterialsPreserved;
            public bool TexturesPresent;
        }

        public static string Execute(string arg)
        {
            RequireScene();
            string command = (arg ?? string.Empty).Trim().ToLowerInvariant();
            switch (command)
            {
                case "inspect": return Inspect();
                case "install": return Install();
                case "validate": return Validate();
                default: throw new ArgumentException("Expected inspect, install, or validate.");
            }
        }

        private static void RequireScene()
        {
            if (EditorApplication.isPlaying
                || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Open W_WorldMacro_Playtest in Edit mode. This tool never rebuilds the world or shared PlayerRig.");
        }

        private static string Inspect()
        {
            var report = BuildReport("inspect", false);
            report.status = SourceReady(report) ? "READY_TO_INSTALL" : "FAILED";
            return JsonUtility.ToJson(report, true);
        }

        private static string Install()
        {
            var before = BuildReport("install", false);
            if (!SourceReady(before)) throw new InvalidOperationException("C02 source is not ready:\n" + string.Join("\n", before.failures));
            EnsureFolders();

            var source = NeedAsset<GameObject>(SourceModelPath);
            var clips = LoadClips();
            Geometry sourceGeometry = Measure(source);
            float scale = TargetHeight / Mathf.Max(.001f, sourceGeometry.Bounds.size.y);
            Vector3 offset = Vector3.up * (-sourceGeometry.Bounds.min.y * scale);
            var controller = BuildController(clips.idle, clips.walk, clips.run);
            var profile = BuildProfile(controller, clips.idle, clips.walk, clips.run, scale, offset);
            BuildOwnedMaterials();
            BuildPrefab(source, profile);
            InstallSceneInstance(profile);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            var report = BuildReport("install", true);
            if (report.failures.Length > 0) throw new InvalidOperationException("Player appearance installed but validation failed:\n" + string.Join("\n", report.failures));
            report.status = "INSTALLED_TECHNICAL_PASS";
            return SaveReport(report);
        }

        private static string Validate()
        {
            var report = BuildReport("validate", true);
            report.status = report.failures.Length == 0 ? "TECHNICAL_PASS" : "FAILED";
            return SaveReport(report);
        }

        private static (AnimationClip idle, AnimationClip walk, AnimationClip run) LoadClips()
        {
            return (NeedAsset<AnimationClip>(IdlePath), NeedAsset<AnimationClip>(WalkPath), NeedAsset<AnimationClip>(RunPath));
        }

        private static AnimatorController BuildController(AnimationClip idle, AnimationClip walk, AnimationClip run)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
            {
                foreach (var child in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                    if (child != controller) Object.DestroyImmediate(child, true);
                controller.layers = Array.Empty<AnimatorControllerLayer>();
                controller.parameters = Array.Empty<AnimatorControllerParameter>();
            }

            controller.AddParameter(WorldMacroPlayerAppearance.PlanarSpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddLayer("C02 Locomotion");
            var layers = controller.layers;
            layers[0].iKPass = true;
            layers[0].defaultWeight = 1f;
            controller.layers = layers;
            var machine = layers[0].stateMachine;
            var state = machine.AddState("B2 Idle Walk Run");
            state.writeDefaultValues = false;
            var tree = new BlendTree
            {
                name = "B2 Planar Speed",
                blendType = BlendTreeType.Simple1D,
                blendParameter = WorldMacroPlayerAppearance.PlanarSpeedParameter,
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.children = new[]
            {
                new ChildMotion { motion = idle, threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = walk, threshold = NativeWalkSpeed, timeScale = 1f },
                new ChildMotion { motion = run, threshold = GameRunSpeed, timeScale = GameRunSpeed / NativeRunSpeed }
            };
            state.motion = tree;
            machine.defaultState = state;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static WorldMacroPlayerAppearanceProfile BuildProfile(AnimatorController controller,
            AnimationClip idle, AnimationClip walk, AnimationClip run, float scale, Vector3 offset)
        {
            RepairProfileScriptBinding();
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerAppearanceProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<WorldMacroPlayerAppearanceProfile>();
                profile.name = "PlayerAppearance_C02_A2B2C3";
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.Controller = controller;
            profile.Idle = idle;
            profile.Walk = walk;
            profile.Run = run;
            profile.ModelHeight = TargetHeight;
            profile.WalkSpeed = NativeWalkSpeed;
            profile.RunSpeed = GameRunSpeed;
            profile.NativeRunSpeed = NativeRunSpeed;
            profile.VelocityDamping = 12f;
            profile.FacingYaw = 0f;
            profile.UniformScale = scale;
            profile.LocalOffset = offset;
            profile.DrawingArmWeight = .32f;
            profile.DrawingReach = .58f;
            profile.BlinkDuration = .16f;
            profile.BlinkInterval = 4.2f;
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void RepairProfileScriptBinding()
        {
            if (!File.Exists(ProfilePath)) return;
            string scriptGuid = AssetDatabase.AssetPathToGUID(ProfileScriptPath);
            if (string.IsNullOrEmpty(scriptGuid))
                throw new FileNotFoundException("Player appearance profile script is not imported: " + ProfileScriptPath);

            string expected = "  m_Script: {fileID: 11500000, guid: " + scriptGuid + ", type: 3}";
            string yaml = File.ReadAllText(ProfilePath);
            Match binding = Regex.Match(yaml, @"(?m)^  m_Script: \{[^\r\n]*\}$");
            if (!binding.Success) throw new InvalidDataException("Owned player profile has no serialized m_Script binding.");
            if (binding.Value == expected) return;
            if (!binding.Value.Contains("fileID: 0"))
                throw new InvalidDataException("Owned player profile is bound to an unexpected script; refusing to replace it.");

            File.WriteAllText(ProfilePath, yaml.Remove(binding.Index, binding.Length).Insert(binding.Index, expected));
            AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static bool ProfileScriptBindingIsValid(WorldMacroPlayerAppearanceProfile profile)
        {
            if (profile == null) return false;
            MonoScript script = MonoScript.FromScriptableObject(profile);
            if (script == null || AssetDatabase.GetAssetPath(script) != ProfileScriptPath) return false;
            string guid = AssetDatabase.AssetPathToGUID(ProfileScriptPath);
            return !string.IsNullOrEmpty(guid) && File.ReadAllText(ProfilePath).Contains(
                "m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}");
        }

        private static void BuildOwnedMaterials()
        {
            string materialFolder = Folder + "/Materials";
            if (!AssetDatabase.IsValidFolder(materialFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
            Shader shader = Shader.Find("Oheangbu/WorldMacroTexturedSurface");
            if (shader == null) throw new InvalidOperationException("WorldMacroTexturedSurface shader is missing.");
            for (int i = 0; i < SourceMaterialPaths.Length; i++)
            {
                Material source = NeedAsset<Material>(SourceMaterialPaths[i]);
                Material owned = AssetDatabase.LoadAssetAtPath<Material>(OwnedMaterialPaths[i]);
                bool create = owned == null;
                if (create) owned = new Material(shader) { name = Path.GetFileNameWithoutExtension(OwnedMaterialPaths[i]), enableInstancing = true };
                owned.shader = shader;
                string baseProperty = new[] { "_BaseMap", "_MainTex" }.FirstOrDefault(source.HasProperty);
                Texture baseMap = baseProperty != null ? source.GetTexture(baseProperty) : source.mainTexture;
                Texture normal = source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null;
                owned.SetTexture("_BaseMap", baseMap);
                owned.SetTexture("_BumpMap", normal);
                if (baseProperty != null)
                {
                    owned.SetTextureScale("_BaseMap", source.GetTextureScale(baseProperty));
                    owned.SetTextureOffset("_BaseMap", source.GetTextureOffset(baseProperty));
                }
                Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
                owned.SetColor("_BaseColor", tint);
                owned.SetFloat("_BumpScale", source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f);
                owned.SetFloat("_Saturation", .78f);
                owned.SetFloat("_AmbientFloor", .58f);
                owned.SetFloat("_LightResponse", .75f);
                owned.SetFloat("_WashStart", 800f);
                owned.SetFloat("_WashEnd", 9500f);
                owned.SetFloat("_WashStrength", .58f);
                owned.SetFloat("_AlphaClip", source.HasProperty("_AlphaClip") ? source.GetFloat("_AlphaClip") : 0f);
                owned.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .5f);
                owned.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2f);
                if (create) AssetDatabase.CreateAsset(owned, OwnedMaterialPaths[i]);
                else EditorUtility.SetDirty(owned);
            }
        }

        private static void ApplyOwnedMaterials(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] slots = renderer.sharedMaterials;
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    string sourcePath = AssetDatabase.GetAssetPath(slots[slot]);
                    int index = Array.IndexOf(SourceMaterialPaths, sourcePath);
                    if (index < 0) throw new InvalidOperationException("Unexpected C02 material in selected model: " + sourcePath);
                    slots[slot] = NeedAsset<Material>(OwnedMaterialPaths[index]);
                }
                renderer.sharedMaterials = slots;
            }
        }

        private static void ValidateOwnedMaterials(GameObject instance, Report report)
        {
            Material[] used = instance.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct().ToArray();
            string[] usedPaths = used.Select(AssetDatabase.GetAssetPath).OrderBy(path => path).ToArray();
            report.playerMaterials = usedPaths;
            report.ownedMaterialPaths = usedPaths.Length == OwnedMaterialPaths.Length
                && OwnedMaterialPaths.All(path => usedPaths.Contains(path));
            report.materialsPreserved = report.ownedMaterialPaths && used.All(material => material.shader != null
                && material.shader.name == "Oheangbu/WorldMacroTexturedSurface");
            report.ambientFloor = used.Length > 0 && used.All(material => material.HasProperty("_AmbientFloor"))
                ? used.Min(material => material.GetFloat("_AmbientFloor")) : 0f;

            bool textureMatch = true;
            for (int i = 0; i < SourceMaterialPaths.Length; i++)
            {
                Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPaths[i]);
                Material owned = AssetDatabase.LoadAssetAtPath<Material>(OwnedMaterialPaths[i]);
                string sourceBaseProperty = source != null
                    ? new[] { "_BaseMap", "_MainTex" }.FirstOrDefault(source.HasProperty) : null;
                Texture sourceBase = sourceBaseProperty != null ? source.GetTexture(sourceBaseProperty) : source != null ? source.mainTexture : null;
                Texture sourceNormal = source != null && source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null;
                textureMatch &= source != null && owned != null && sourceBase != null
                    && owned.GetTexture("_BaseMap") == sourceBase && owned.GetTexture("_BumpMap") == sourceNormal;
            }
            report.ownedTexturesMatchSources = textureMatch;
            report.texturesPresent = textureMatch;
            report.materialsPreserved &= textureMatch && report.ambientFloor >= .58f - .001f;
        }

        private static void BuildPrefab(GameObject source, WorldMacroPlayerAppearanceProfile profile)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (root == null) throw new InvalidOperationException("Could not instantiate selected C02 source model.");
            root.name = "PF_C02_A2B2C3_Playtest";
            try
            {
                root.transform.localPosition = profile.LocalOffset;
                root.transform.localRotation = Quaternion.Euler(0f, profile.FacingYaw, 0f);
                root.transform.localScale = Vector3.one * profile.UniformScale;
                var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
                animator.runtimeAnimatorController = profile.Controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skin.quality = SkinQuality.Auto;
                ApplyOwnedMaterials(root);
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                var appearance = root.GetComponent<WorldMacroPlayerAppearance>() ?? root.AddComponent<WorldMacroPlayerAppearance>();
                appearance.Configure(profile, animator, null, null, null, null, null, renderers);
                if (root.GetComponentsInChildren<Cloth>(true).Length > 0)
                    throw new InvalidOperationException("Selected derivative unexpectedly contains Cloth.");
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                    throw new IOException("Could not save playtest player appearance prefab.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void InstallSceneInstance(WorldMacroPlayerAppearanceProfile profile)
        {
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            if (walker == null || walker.Body == null) throw new InvalidOperationException("Macro combat walker/body is missing.");
            Transform player = walker.Body.transform;
            var motor = player.GetComponent<PlayerMotor>();
            var drawing = player.root.GetComponentInChildren<DrawingInputController>(true);
            var cameraRig = player.GetComponent<CameraRigController>();
            var brushFeed = player.root.GetComponentInChildren<BrushStrokeFeedAdapter>(true);
            if (motor == null || drawing == null || cameraRig == null || brushFeed == null)
                throw new InvalidOperationException("Macro player is missing motor, drawing, camera, or brush presentation binding.");

            Transform existing = player.Find(SceneRootName);
            if (existing != null)
            {
                if (existing.GetComponent<WorldMacroPlayerAppearance>() == null)
                    throw new InvalidOperationException("Owned player appearance name is occupied by another object.");
            }

            var prefab = NeedAsset<GameObject>(PrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player);
            if (instance == null) throw new InvalidOperationException("Could not instantiate owned player appearance prefab.");
            WorldMacroPlayerAppearance appearance;
            Renderer[] renderers;
            try
            {
                instance.name = SceneRootName + "_Candidate";
                instance.transform.localPosition = profile.LocalOffset;
                instance.transform.localRotation = Quaternion.Euler(0f, profile.FacingYaw, 0f);
                instance.transform.localScale = Vector3.one * profile.UniformScale;
                var animator = instance.GetComponent<Animator>();
                appearance = instance.GetComponent<WorldMacroPlayerAppearance>();
                if (animator == null || appearance == null)
                    throw new InvalidOperationException("Owned player appearance is missing its Animator or runtime adapter.");
                renderers = instance.GetComponentsInChildren<Renderer>(true);
                appearance.Configure(profile, animator, walker.Body, motor, drawing, cameraRig, brushFeed, renderers);
            }
            catch
            {
                Object.DestroyImmediate(instance);
                throw;
            }

            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            instance.name = SceneRootName;
            cameraRig.SetBodyRenderers(renderers);
            walker.Visuals = renderers;

            Transform primitive = player.Find("Body");
            if (primitive != null)
            {
                var renderer = primitive.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    EditorUtility.SetDirty(renderer);
                }
            }
            EditorUtility.SetDirty(appearance);
            EditorUtility.SetDirty(cameraRig);
            EditorUtility.SetDirty(walker);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        private static Report BuildReport(string command, bool requireOwnedAssets)
        {
            var failures = new System.Collections.Generic.List<string>();
            var report = new Report
            {
                command = command,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                model = SourceModelPath,
                prefab = PrefabPath,
                controller = ControllerPath,
                profile = ProfilePath
            };
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelPath);
            report.sourceModelFound = source != null;
            if (source == null) failures.Add("Selected model missing: " + SourceModelPath);
            var sourceAnimator = source != null ? source.GetComponent<Animator>() : null;
            var avatar = sourceAnimator != null ? sourceAnimator.avatar : null;
            report.avatarValid = avatar != null && avatar.isValid;
            report.avatarHuman = avatar != null && avatar.isHuman;
            if (!report.avatarValid || !report.avatarHuman) failures.Add("Selected model needs its own valid Humanoid Avatar.");

            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath);
            var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunPath);
            report.clipsFound = idle != null && walk != null && run != null;
            report.clipsHuman = report.clipsFound && idle.humanMotion && walk.humanMotion && run.humanMotion;
            if (!report.clipsFound) failures.Add("One or more selected B2 locomotion clips are missing.");
            else if (!report.clipsHuman) failures.Add("Selected B2 locomotion clips must be Humanoid clips.");

            if (source != null)
            {
                Geometry geometry = Measure(source);
                report.triangles = geometry.Triangles;
                report.renderers = geometry.Renderers;
                report.materials = geometry.Materials;
                report.blinkChannels = geometry.BlinkChannels;
                report.materialsPreserved = geometry.MaterialsPreserved;
                report.texturesPresent = geometry.TexturesPresent;
                report.triangleCountMatches = geometry.Triangles == ExpectedTriangles;
                if (!report.triangleCountMatches) failures.Add("Selected model triangle count changed; expected 52784, got " + geometry.Triangles + ".");
                if (geometry.BlinkChannels != 2) failures.Add("Selected C3 model needs BlinkLeft and BlinkRight; found " + geometry.BlinkChannels + " channels.");
                if (!report.materialsPreserved || !report.texturesPresent) failures.Add("C02 source materials or base-color textures are not preserved.");
            }

            if (requireOwnedAssets)
            {
                if (File.Exists(ProfilePath))
                    AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerAppearanceProfile>(ProfilePath);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (prefab == null || profile == null || controller == null) failures.Add("Owned prefab, profile, or controller is missing.");
                report.profileScriptBound = ProfileScriptBindingIsValid(profile);
                if (!report.profileScriptBound) failures.Add("Owned player profile MonoScript binding did not survive a forced asset reload.");
                report.speedParameterLinked = controller != null && controller.parameters.Any(p =>
                    p.name == WorldMacroPlayerAppearance.PlanarSpeedParameter && p.type == AnimatorControllerParameterType.Float)
                    && controller.layers.SelectMany(l => l.stateMachine.states).Any(s =>
                        s.state.motion is BlendTree tree && tree.blendParameter == WorldMacroPlayerAppearance.PlanarSpeedParameter
                        && tree.children.Length == 3 && tree.children.All(c => c.motion != null));
                if (!report.speedParameterLinked) failures.Add("PlanarSpeed is not linked to the three B2 locomotion clips.");

                if (prefab != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        var animator = instance.GetComponent<Animator>();
                        report.rootMotionOff = animator != null && !animator.applyRootMotion;
                        report.noCloth = instance.GetComponentsInChildren<Cloth>(true).Length == 0;
                        report.requiredBones = animator != null && animator.isHuman && RequiredBones().All(b => animator.GetBoneTransform(b) != null);
                        report.fingerBonesPresent = animator != null && animator.isHuman
                            && animator.GetBoneTransform(HumanBodyBones.RightIndexProximal) != null;
                        Geometry geometry = MeasureInstance(instance);
                        report.renderedHeight = geometry.Bounds.size.y;
                        report.footPlane = geometry.Bounds.min.y;
                        report.uniformScale = instance.transform.localScale.x;
                        report.heightMatches = Mathf.Abs(report.renderedHeight - TargetHeight) <= .02f;
                        report.footPlaneMatches = Mathf.Abs(report.footPlane) <= .02f;
                        report.triangleCountMatches = geometry.Triangles == ExpectedTriangles;
                        ValidateOwnedMaterials(instance, report);
                        if (!report.rootMotionOff) failures.Add("Owned Animator root motion must be off.");
                        if (!report.noCloth) failures.Add("Owned player appearance must not contain Cloth.");
                        if (!report.requiredBones) failures.Add("Owned Humanoid is missing a required movement or drawing-arm bone.");
                        if (!report.triangleCountMatches) failures.Add("Owned prefab does not preserve the selected model's 52784 triangles.");
                        if (geometry.BlinkChannels != 2) failures.Add("Owned prefab does not preserve both C3 blink channels.");
                        if (!report.materialsPreserved || !report.texturesPresent) failures.Add("Owned prefab does not preserve C02 materials and textures.");
                        if (!report.heightMatches || !report.footPlaneMatches) failures.Add("Owned model does not match the 1.75m capsule foot plane.");
                    }
                    finally { Object.DestroyImmediate(instance); }
                }

                var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
                var appearance = walker != null && walker.Body != null
                    ? walker.Body.transform.Find(SceneRootName)?.GetComponent<WorldMacroPlayerAppearance>() : null;
                report.installed = appearance != null;
                if (!report.installed) failures.Add("Playtest player appearance is not installed under the inline combat player.");
                if (walker != null && appearance != null)
                {
                    var renderers = appearance.WorldRenderers.Where(r => r != null).ToArray();
                    report.walkerRendererContract = SameRenderers(walker.Visuals, renderers);
                    var cameraRig = walker.Body.GetComponent<CameraRigController>();
                    report.cameraRendererContract = CameraRenderers(cameraRig, renderers);
                    Transform primitive = walker.Body.transform.Find("Body");
                    var primitiveRenderer = primitive != null ? primitive.GetComponent<Renderer>() : null;
                    report.primitiveRendererDisabled = primitiveRenderer == null || !primitiveRenderer.enabled;
                    var body = walker.Body;
                    report.colliderMatches = Mathf.Abs(body.height - 1.75f) < .001f
                        && Mathf.Abs(body.radius - .28f) < .001f && Mathf.Abs(body.center.y - .875f) < .001f;
                    if (!report.walkerRendererContract) failures.Add("Palanquin walker renderer visibility does not own the C02 renderers.");
                    if (!report.cameraRendererContract) failures.Add("Camera body visibility does not own the C02 renderers.");
                    if (!report.primitiveRendererDisabled) failures.Add("Legacy primitive player renderer is still enabled.");
                    if (!report.colliderMatches) failures.Add("Existing macro CharacterController dimensions changed.");
                }
            }
            report.failures = failures.ToArray();
            return report;
        }

        private static bool SourceReady(Report report)
        {
            return report.sourceModelFound && report.avatarValid && report.avatarHuman && report.clipsFound
                && report.clipsHuman && report.triangleCountMatches && report.blinkChannels == 2
                && report.materialsPreserved && report.texturesPresent;
        }

        private static Geometry Measure(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) throw new InvalidOperationException("Could not measure prefab " + prefab.name + ".");
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                return MeasureInstance(instance);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static Geometry MeasureInstance(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true).Where(r => r != null).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("Player appearance has no renderers.");
            Bounds bounds = renderers[0].bounds;
            int triangles = 0, materials = 0, blink = 0;
            bool materialPaths = true, textures = true;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null)
                {
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        if (mesh.GetTopology(submesh) == MeshTopology.Triangles) triangles += (int)mesh.GetIndexCount(submesh) / 3;
                    for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                        if (mesh.GetBlendShapeName(shape) == "BlinkLeft" || mesh.GetBlendShapeName(shape) == "BlinkRight") blink++;
                }
                materials += renderer.sharedMaterials.Length;
                foreach (var material in renderer.sharedMaterials)
                {
                    string materialPath = material != null ? AssetDatabase.GetAssetPath(material) : string.Empty;
                    materialPaths &= material != null && materialPath.StartsWith(
                        "Assets/_Project/Art/C02_RigFaceLab/Materials/", StringComparison.Ordinal);
                    Texture texture = material != null && material.HasProperty("_BaseMap")
                        ? material.GetTexture("_BaseMap") : material != null ? material.mainTexture : null;
                    string texturePath = texture != null ? AssetDatabase.GetAssetPath(texture) : string.Empty;
                    textures &= texture != null && texturePath.StartsWith(
                        "Assets/_Project/Art/C02_RigFaceLab/Textures/", StringComparison.Ordinal);
                }
            }
            return new Geometry
            {
                Bounds = bounds,
                Triangles = triangles,
                Renderers = renderers.Length,
                Materials = materials,
                BlinkChannels = blink,
                MaterialsPreserved = materialPaths,
                TexturesPresent = textures
            };
        }

        private static bool CameraRenderers(CameraRigController cameraRig, Renderer[] expected)
        {
            if (cameraRig == null) return false;
            var property = new SerializedObject(cameraRig).FindProperty("_bodyRenderers");
            if (property == null || property.arraySize != expected.Length) return false;
            var actual = Enumerable.Range(0, property.arraySize)
                .Select(i => property.GetArrayElementAtIndex(i).objectReferenceValue as Renderer).ToArray();
            return SameRenderers(actual, expected);
        }

        private static bool SameRenderers(Renderer[] left, Renderer[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            return left.All(right.Contains) && right.All(left.Contains);
        }

        private static HumanBodyBones[] RequiredBones()
        {
            return new[]
            {
                HumanBodyBones.Hips, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
            };
        }

        private static T NeedAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(typeof(T).Name + " missing", path);
            return asset;
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(WorldMacroPlaytestAuthoring.Output + "/PlayerAppearance");
        }

        private static string SaveReport(Report report)
        {
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(WorldMacroPlaytestAuthoring.Output + "/PlayerAppearance");
            File.WriteAllText(WorldMacroPlaytestAuthoring.Output + "/PlayerAppearance/validation.json", json);
            return json;
        }
    }
}
