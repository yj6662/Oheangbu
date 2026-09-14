using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Data;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Repeatable asset import and staged PlayerRig installation. SPEC-PLAYER-DOSA.</summary>
    public static class DosaPlayerBuilder
    {
        public const string Folder = "Assets/_Project/Art/Characters/Dosa";
        public const string WorldModel = Folder + "/Models/SM_DosaCourier_Refined.fbx";
        public const string ArmsModel = Folder + "/Models/SM_DosaCourier_Arms.fbx";
        public const string ControllerPath = Folder + "/AC_DosaCourier.controller";
        public const string VisualPath = "Assets/_Project/Data/Player/PlayerVisual_Dosa.asset";
        public const string DrawingPath = "Assets/_Project/Data/Player/DrawingPose_Dosa.asset";
        public const string ValidationScene = "Assets/_Project/Scenes/Dev/C2_PlayerValidation.unity";
        public const string VisualPrefabPath = "Assets/_Project/Prefabs/Player/PF_DosaVisual.prefab";
        public const int ViewmodelLayer = 31;
        public static readonly string[] MotionNames = { "Idle", "CombatReady", "WalkForward", "WalkBack", "WalkLeft", "WalkRight",
            "RunForward", "RunBack", "RunLeft", "RunRight", "DodgeForward", "DodgeBack", "DodgeLeft", "DodgeRight", "DrawReady", "DrawHold", "DrawRelease" };

        [MenuItem("Oheangbu/Player/1. Import Dosa assets")]
        private static void ImportMenu() => Debug.Log(ImportAssets());
        [MenuItem("Oheangbu/Player/2. Build isolated validation scene")]
        private static void ValidationMenu() => Debug.Log(BuildValidation());
        [MenuItem("Oheangbu/Player/3. Apply to canonical C2")]
        private static void C2Menu() => Debug.Log(ApplyToC2());
        [MenuItem("Oheangbu/Player/4. Publish to shared PlayerRig")]
        private static void PublishMenu() => Debug.Log(PublishSharedRig());

        public static string ImportAssets() => ImportSelected(true);
        public static string ImportCharacterModels() => ImportSelected(false);
        private static string ImportSelected(bool includeAnimations)
        {
            RequireEdit();
            string[] paths = includeAnimations ? new[] { WorldModel, ArmsModel }.Concat(MotionNames.Select(ClipPath)).ToArray() : new[] { WorldModel, ArmsModel };
            var missing = paths.Where(p => !File.Exists(p)).ToArray();
            if (missing.Length != 0) return "WAIT: missing " + string.Join(", ", missing);
            foreach (string path in paths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("Model importer missing " + path);
                importer.globalScale = 1f;
                importer.useFileScale = true;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importVisibility = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.isReadable = true;
                importer.optimizeGameObjects = false;
                importer.importBlendShapes = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = path.Contains("/Animations/");
                importer.humanDescription = HumanDescriptionFor(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                if (importer.importAnimation)
                {
                    var clips = importer.defaultClipAnimations;
                    if (clips.Length == 0) throw new InvalidOperationException("No animation in " + path);
                    string name = Path.GetFileNameWithoutExtension(path).Replace("A_DosaCourier_", "");
                    clips = new[] { clips[0] };
                    clips[0].name = name;
                    clips[0].loopTime = !name.StartsWith("Dodge", StringComparison.Ordinal) && name != "DrawReady" && name != "DrawRelease";
                    clips[0].loopPose = clips[0].loopTime;
                    clips[0].lockRootRotation = true;
                    clips[0].lockRootPositionXZ = true;
                    clips[0].lockRootHeightY = true;
                    clips[0].keepOriginalOrientation = true;
                    clips[0].keepOriginalPositionXZ = true;
                    clips[0].keepOriginalPositionY = true;
                    importer.clipAnimations = clips;
                }
                importer.SaveAndReimport();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Invalid Humanoid " + path);
            }
            string texture = Folder + "/Textures/T_DosaCourier_BaseColor.png";
            AssetDatabase.ImportAsset(texture, ImportAssetOptions.ForceSynchronousImport);
            var textureImporter = AssetImporter.GetAtPath(texture) as TextureImporter;
            if (textureImporter == null) throw new InvalidOperationException("Missing canonical base color");
            textureImporter.sRGBTexture = true;
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.maxTextureSize = 2048;
            textureImporter.SaveAndReimport();
            string handsTexture = Folder + "/Textures/T_DosaCourier_HandsBaseColor.png";
            AssetDatabase.ImportAsset(handsTexture, ImportAssetOptions.ForceSynchronousImport);
            var handsImporter = AssetImporter.GetAtPath(handsTexture) as TextureImporter;
            if (handsImporter == null) throw new InvalidOperationException("Missing dedicated hand texture");
            handsImporter.sRGBTexture = true; handsImporter.textureType = TextureImporterType.Default; handsImporter.maxTextureSize = 1024;
            handsImporter.SaveAndReimport();
            EnsureMaterial();
            EnsureHandMaterial();
            EnsureProfiles();
            if (!includeAnimations) { AssetDatabase.SaveAssets(); return "OK: models imported. " + AuditAssets(); }
            BuildController();
            BuildVisualPrefab();
            AssetDatabase.SaveAssets();
            return "OK: 2 Humanoid models, 17 motion files, profiles/controller/visual prefab. " + AuditAssets();
        }

        private static HumanDescription HumanDescriptionFor(GameObject model)
        {
            if (model == null) throw new InvalidOperationException("Model not imported");
            var bones = model.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>(bones.Select(b => b.name), StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<HumanBodyBones, string>
            {
                {HumanBodyBones.Hips,"Hips"},{HumanBodyBones.Spine,"Spine"},{HumanBodyBones.Chest,"Spine01"},{HumanBodyBones.UpperChest,"Spine02"},
                {HumanBodyBones.Neck,"Neck"},{HumanBodyBones.Head,"Head"},
                {HumanBodyBones.LeftShoulder,"LeftShoulder"},{HumanBodyBones.RightShoulder,"RightShoulder"},
                {HumanBodyBones.LeftUpperArm,"LeftArm"},{HumanBodyBones.RightUpperArm,"RightArm"},
                {HumanBodyBones.LeftLowerArm,"LeftForeArm"},{HumanBodyBones.RightLowerArm,"RightForeArm"},
                {HumanBodyBones.LeftHand,"LeftHand"},{HumanBodyBones.RightHand,"RightHand"},
                {HumanBodyBones.LeftUpperLeg,"LeftUpLeg"},{HumanBodyBones.RightUpperLeg,"RightUpLeg"},
                {HumanBodyBones.LeftLowerLeg,"LeftLeg"},{HumanBodyBones.RightLowerLeg,"RightLeg"},
                {HumanBodyBones.LeftFoot,"LeftFoot"},{HumanBodyBones.RightFoot,"RightFoot"},
                {HumanBodyBones.LeftToes,"LeftToeBase"},{HumanBodyBones.RightToes,"RightToeBase"}
            };
            foreach (string side in new[] { "Left", "Right" })
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                int index = 1;
                foreach (string joint in new[] { "Proximal", "Intermediate", "Distal" })
                {
                    var human = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + joint);
                    string source = side + "Hand" + (finger == "Little" ? "Pinky" : finger) + index++;
                    map[human] = source;
                }
            }
            return new HumanDescription
            {
                human = map.Where(kv => names.Contains(kv.Value)).Select(kv => new HumanBone
                {
                    humanName = HumanTrait.BoneName[(int)kv.Key], boneName = bones.First(b => string.Equals(b.name, kv.Value, StringComparison.OrdinalIgnoreCase)).name,
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray(),
                skeleton = bones.Select(b => new SkeletonBone { name = b.name, position = b.localPosition, rotation = b.localRotation, scale = b.localScale }).ToArray(),
                armStretch = .05f, legStretch = .05f, upperArmTwist = .5f, lowerArmTwist = .5f,
                upperLegTwist = .5f, lowerLegTwist = .5f, feetSpacing = 0f, hasTranslationDoF = false
            };
        }

        private static void EnsureProfiles()
        {
            EnsureAsset<PlayerVisualProfileSO>(VisualPath);
            EnsureAsset<DrawingPoseProfileSO>(DrawingPath);
        }
        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var item = AssetDatabase.LoadAssetAtPath<T>(path);
            if (item != null) return item;
            DevSceneKit.EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));
            item = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(item, path);
            return item;
        }
        private static Material EnsureMaterial()
        {
            string path = Folder + "/Materials/M_DosaCourier.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            DevSceneKit.EnsureFolder(Folder + "/Materials");
            var shader = Shader.Find("Oheangbu/DesaturatedAssetLit");
            if (shader == null) throw new InvalidOperationException("DesaturatedAssetLit missing");
            mat = new Material(shader) { name = "M_DosaCourier" };
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/T_DosaCourier_BaseColor.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AssetSaturation", .12f);
            mat.SetFloat("_AssetAmbient", .4f);
            mat.SetFloat("_AssetValue", 1.05f);
            mat.SetFloat("_Smoothness", .16f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
        public static string ClipPath(string name) => Folder + "/Animations/A_DosaCourier_" + name + ".fbx";
        private static Material EnsureHandMaterial()
        {
            string path = Folder + "/Materials/M_DosaCourier_Hands.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(EnsureMaterial()) { name = "M_DosaCourier_Hands" };
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/T_DosaCourier_HandsBaseColor.png"));
            AssetDatabase.CreateAsset(mat, path); return mat;
        }
        private static AnimationClip Clip(string name)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(ClipPath(name)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) throw new InvalidOperationException("Missing clip " + name);
            return clip;
        }

        public static void BuildController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
            {
                // Rebuild only this generated controller; keep its GUID for prefab references.
                foreach (var child in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                    if (child != controller) Object.DestroyImmediate(child, true);
                controller.layers = Array.Empty<AnimatorControllerLayer>();
                controller.parameters = Array.Empty<AnimatorControllerParameter>();
            }
            foreach (string p in new[] { "MoveX", "MoveY", "Speed", "DodgeX", "DodgeY", "DodgeProgress" }) controller.AddParameter(p, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "DrawPlaybackRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter(new AnimatorControllerParameter { name = "LocomotionPlaybackRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            foreach (string p in new[] { "Combat", "Drawing", "Dodge" }) controller.AddParameter(p, AnimatorControllerParameterType.Bool);
            controller.AddLayer("Locomotion");
            var machine = controller.layers[0].stateMachine;
            var normal = machine.AddState("Locomotion");
            var ready = machine.AddState("CombatLocomotion");
            normal.motion = MovementTree(controller, "Idle");
            ready.motion = MovementTree(controller, "CombatReady");
            foreach (var state in new[] { normal, ready }) { state.speedParameter = "LocomotionPlaybackRate"; state.speedParameterActive = true; }
            machine.defaultState = normal;
            Transition(normal, ready, "Combat", true, .12f);
            Transition(ready, normal, "Combat", false, .15f);
            var dodge = machine.AddState("Dodge");
            var dodgeTree = NewTree(controller, "DirectionalDodge", "DodgeX", "DodgeY");
            dodgeTree.AddChild(Clip("DodgeForward"), Vector2.up);
            dodgeTree.AddChild(Clip("DodgeBack"), Vector2.down);
            dodgeTree.AddChild(Clip("DodgeLeft"), Vector2.left);
            dodgeTree.AddChild(Clip("DodgeRight"), Vector2.right);
            dodge.motion = dodgeTree;
            dodge.timeParameter = "DodgeProgress";
            dodge.timeParameterActive = true;
            var any = machine.AddAnyStateTransition(dodge); any.hasExitTime = false; any.duration = .03f; any.canTransitionToSelf = false;
            any.AddCondition(AnimatorConditionMode.If, 0, "Dodge");
            Transition(dodge, normal, "Dodge", false, .08f);

            controller.AddLayer("Drawing posture");
            var layers = controller.layers;
            var mask = new AvatarMask { name = "DosaDrawingUpperBody" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers }) mask.SetHumanoidBodyPartActive(part, true);
            AssetDatabase.AddObjectToAsset(mask, controller);
            // A weight-one empty Humanoid layer still writes an upper-body reference pose.
            // PlayerVisualRig owns this weight during drawing entry/recovery; locomotion starts unmasked.
            layers[1].avatarMask = mask; layers[1].defaultWeight = 0f; layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;
            var draw = layers[1].stateMachine;
            var empty = draw.AddState("None"); empty.writeDefaultValues = false;
            var enter = draw.AddState("DrawReady"); enter.motion = Clip("DrawReady"); enter.writeDefaultValues = false;
            var hold = draw.AddState("DrawHold"); hold.motion = Clip("DrawHold"); hold.writeDefaultValues = false;
            var leave = draw.AddState("DrawRelease"); leave.motion = Clip("DrawRelease"); leave.writeDefaultValues = false;
            foreach (var state in new[] { enter, hold, leave }) { state.speedParameter = "DrawPlaybackRate"; state.speedParameterActive = true; }
            draw.defaultState = empty;
            Transition(empty, enter, "Drawing", true, .1f);
            var toHold = enter.AddTransition(hold); toHold.hasExitTime = true; toHold.exitTime = .85f; toHold.duration = .08f;
            toHold.AddCondition(AnimatorConditionMode.If, 0, "Drawing");
            Transition(enter, leave, "Drawing", false, .08f);
            Transition(hold, leave, "Drawing", false, .08f);
            Transition(leave, enter, "Drawing", true, .05f);
            var toEmpty = leave.AddTransition(empty); toEmpty.hasExitTime = true; toEmpty.exitTime = .9f; toEmpty.duration = .15f;
            toEmpty.AddCondition(AnimatorConditionMode.IfNot, 0, "Drawing");
            var profile = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(VisualPath);
            profile.Controller = controller; EditorUtility.SetDirty(profile); EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }
        private static BlendTree MovementTree(AnimatorController controller, string idle)
        {
            var tree = NewTree(controller, idle + "Movement", "MoveX", "MoveY");
            tree.AddChild(Clip(idle), Vector2.zero);
            var p = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(VisualPath);
            foreach (string gait in new[] { "Walk", "Run" })
            {
                float speed = gait == "Walk" ? p.WalkClipSpeed : p.RunClipSpeed;
                tree.AddChild(Clip(gait + "Forward"), Vector2.up * speed); tree.AddChild(Clip(gait + "Back"), Vector2.down * speed);
                tree.AddChild(Clip(gait + "Left"), Vector2.left * speed); tree.AddChild(Clip(gait + "Right"), Vector2.right * speed);
                Vector4 measured = gait == "Walk" ? p.WalkSourceSpeeds : p.RunSourceSpeeds;
                var children = tree.children;
                for (int direction = 0; direction < 4; direction++) children[children.Length - 4 + direction].timeScale = speed / Mathf.Max(.1f, measured[direction]);
                tree.children = children;
            }
            return tree;
        }
        private static BlendTree NewTree(AnimatorController c, string name, string x, string y)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.FreeformDirectional2D, blendParameter = x, blendParameterY = y, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, c); return tree;
        }
        private static void Transition(AnimatorState from, AnimatorState to, string parameter, bool condition, float duration)
        {
            var t = from.AddTransition(to); t.hasExitTime = false; t.duration = duration;
            t.AddCondition(condition ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static void BuildVisualPrefab()
        {
            DevSceneKit.EnsureFolder("Assets/_Project/Prefabs/Player");
            var root = new GameObject("DosaVisual");
            try
            {
                var world = MakeModel(WorldModel, "WorldBody", root.transform);
                var near = MakeModel(ArmsModel, "NearArms", root.transform);
                var worldBrush = MakeBrush("WorldBrush", root.transform);
                var nearBrush = MakeBrush("NearBrush", root.transform);
                SetLayer(near.transform, ViewmodelLayer); SetLayer(nearBrush, ViewmodelLayer);
                foreach (var r in near.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (var r in nearBrush.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                var rig = root.AddComponent<PlayerVisualRig>();
                rig.Configure(world, near, worldBrush, nearBrush,
                    AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(VisualPath), AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>(DrawingPath));
                PrefabUtility.SaveAsPrefabAsset(root, VisualPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Animator MakeModel(string path, string name, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().First(a => a.isHuman && a.isValid);
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var material = EnsureMaterial();
            foreach (var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = i == 1 ? EnsureHandMaterial() : material;
                renderer.sharedMaterials = materials;
                renderer.updateWhenOffscreen = true;
            }
            return animator;
        }
        private static Transform MakeBrush(string name, Transform parent)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            var grip = new GameObject("GripSocket").transform; grip.SetParent(root, false);
            var tip = new GameObject("TipSocket").transform; tip.SetParent(root, false); tip.localPosition = Vector3.up * .28f;
            var wood = DevSceneKit.EnsureMaterial("M_DosaBrushWood", new Color(.17f, .13f, .095f));
            var hair = DevSceneKit.EnsureMaterial("M_DosaBrushHair", new Color(.07f, .062f, .05f));
            BrushPart(root, "Handle", -.14f, .19f, .009f, .009f, wood);
            BrushPart(root, "Ferrule", .175f, .205f, .011f, .013f, hair);
            BrushPart(root, "Bristles", .20f, .28f, .014f, .0005f, hair);
            return root;
        }
        private static void BrushPart(Transform parent, string name, float bottom, float top, float r0, float r1, Material material)
        {
            const int sides = 16;
            var vertices = new Vector3[sides * 2 + 2]; var uv = new Vector2[vertices.Length]; var triangles = new int[sides * 12];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides; var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[i] = radial * r0 + Vector3.up * bottom; vertices[i + sides] = radial * r1 + Vector3.up * top;
                uv[i] = new Vector2((float)i / sides, 0); uv[i + sides] = new Vector2((float)i / sides, 1);
                int j = (i + 1) % sides, k = i * 12;
                triangles[k] = i; triangles[k+1] = i+sides; triangles[k+2] = j;
                triangles[k+3] = j; triangles[k+4] = i+sides; triangles[k+5] = j+sides;
                triangles[k+6] = sides*2; triangles[k+7] = i; triangles[k+8] = j;
                triangles[k+9] = sides*2+1; triangles[k+10] = j+sides; triangles[k+11] = i+sides;
            }
            vertices[sides*2] = Vector3.up * bottom; vertices[sides*2+1] = Vector3.up * top;
            string path = Folder + "/Models/Brush_" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "Brush_" + name }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        public static string BuildValidation()
        {
            RequireEdit();
            // A failed first build can leave our unsaved validation fixture open.
            var active = EditorSceneManager.GetActiveScene();
            if (!(string.IsNullOrEmpty(active.path) && active.GetRootGameObjects().Any(g => g.name == "ValidationFloor"))) RequireSavedScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var oldCamera = Object.FindFirstObjectByType<Camera>(); if (oldCamera != null) Object.DestroyImmediate(oldCamera.gameObject);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "ValidationFloor"; floor.transform.position = new Vector3(0,-.1f,0); floor.transform.localScale = new Vector3(30,.2f,30);
            floor.GetComponent<Renderer>().sharedMaterial = DevSceneKit.EnsureMaterial("M_DosaValidationFloor", new Color(.27f,.25f,.22f));
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DevSceneKit.RigPrefabPath), scene);
            root.transform.position = new Vector3(0,1.08f,0); root.transform.rotation = Quaternion.identity;
            Install(root);
            for (int i = 0; i < 5; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube); step.name = "ValidationStep_" + i;
                step.transform.position = new Vector3(3f, .07f * (i+1), 2f + i*.5f); step.transform.localScale = new Vector3(2,.14f*(i+1),.5f);
                step.GetComponent<Renderer>().sharedMaterial = floor.GetComponent<Renderer>().sharedMaterial;
            }
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.42f,.42f,.42f);
            var camera = root.GetComponentInChildren<Camera>(true); camera.backgroundColor = new Color(.45f,.43f,.39f); camera.clearFlags = CameraClearFlags.SolidColor;
            EditorSceneManager.SaveScene(scene, ValidationScene);
            return "OK: isolated validation scene saved; shared prefab untouched. " + AuditScene();
        }
        public static string ApplyToC2()
        {
            RequireEdit(); RequireSavedScene();
            var scene = EditorSceneManager.OpenScene(CodexWorldSceneBuilder.ScenePath, OpenSceneMode.Single);
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            if (motor == null) throw new InvalidOperationException("C2 PlayerMotor missing");
            Install(motor.transform.root.gameObject);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "OK: canonical C2 instance updated. " + AuditScene();
        }
        public static string PublishSharedRig()
        {
            RequireEdit(); RequireSavedScene();
            string returnPath = EditorSceneManager.GetActiveScene().path;
            var root = PrefabUtility.LoadPrefabContents(DevSceneKit.RigPrefabPath);
            try { Install(root); PrefabUtility.SaveAsPrefabAsset(root, DevSceneKit.RigPrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            // Both independently installed scenes can retain temporary overrides after the shared asset changes.
            foreach (string path in new[] { returnPath, CodexWorldSceneBuilder.ScenePath, ValidationScene }.Distinct())
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                var scene = EditorSceneManager.GetActiveScene().path == path ? EditorSceneManager.GetActiveScene()
                    : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                RebindSharedScene(scene);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            if (EditorSceneManager.GetActiveScene().path != returnPath)
                EditorSceneManager.OpenScene(returnPath, OpenSceneMode.Single);
            return "OK: shared PlayerRig and installed scenes updated. " + PlayerRigPrefabTool.CheckAssetSeam() + "; " + AuditScene();
        }
        private static void RebindSharedScene(Scene scene)
        {
            foreach (var motor in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerMotor>(true)))
            {
                var visuals = motor.GetComponentsInChildren<PlayerVisualRig>(true);
                foreach (var visual in visuals)
                    if (PrefabUtility.IsAddedGameObjectOverride(visual.gameObject)) Object.DestroyImmediate(visual.gameObject);
                var drivers = motor.GetComponents<PlayerVisualDriver>();
                foreach (var driver in drivers)
                    if (PrefabUtility.IsAddedComponentOverride(driver)) Object.DestroyImmediate(driver);
                var rootInstance = motor.transform.root.gameObject;
                foreach (var camera in rootInstance.GetComponentsInChildren<Camera>(true))
                    if (camera.name == "DosaArmsCamera" && PrefabUtility.IsAddedGameObjectOverride(camera.gameObject))
                        Object.DestroyImmediate(camera.gameObject);
                var sharedVisual = motor.GetComponentInChildren<PlayerVisualRig>(true);
                if (sharedVisual == null) throw new InvalidOperationException("Shared visual did not propagate to " + motor.name);
                WirePresentation(rootInstance, motor, sharedVisual);
            }
        }
        public static void Install(GameObject playerRig)
        {
            var motor = playerRig.GetComponentInChildren<PlayerMotor>(true);
            if (motor == null) throw new InvalidOperationException("No motor in rig");
            var controller = motor.GetComponent<CharacterController>();
            var old = motor.transform.Find("DosaVisual");
            GameObject go;
            if (old != null && old.GetComponent<PlayerVisualRig>() != null) go = old.gameObject;
            else
            {
                if (old != null) Object.DestroyImmediate(old.gameObject);
                go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath), motor.transform);
            }
            // The controller rests one skin width above a flat collider; the model sole is authored at zero.
            go.name = "DosaVisual"; go.transform.localPosition = Vector3.up * (controller.center.y - controller.height * .5f - controller.skinWidth); go.transform.localRotation = Quaternion.identity;
            Record(go.transform);
            foreach (string name in new[] { "Body", "BrushProp" })
            {
                var part = motor.transform.Find(name);
                if (part != null) { part.gameObject.SetActive(false); Record(part.gameObject); }
            }
            var visual = go.GetComponent<PlayerVisualRig>();
            WirePresentation(playerRig, motor, visual);
        }
        private static void WirePresentation(GameObject playerRig, PlayerMotor motor, PlayerVisualRig visual)
        {
            EnsureViewmodelCamera(playerRig, visual);
            var driver = motor.GetComponent<PlayerVisualDriver>();
            if (driver == null) driver = motor.gameObject.AddComponent<PlayerVisualDriver>();
            driver.Configure(visual, motor.GetComponent<CharacterController>(), motor, playerRig.GetComponentInChildren<DodgeAction>(true), playerRig.GetComponentInChildren<LockOn>(true),
                playerRig.GetComponentInChildren<DrawingInputController>(true), playerRig.GetComponentInChildren<CameraRigController>(true), playerRig.GetComponentInChildren<BrushStrokeFeedAdapter>(true));
            Record(driver);
            foreach (var effect in playerRig.GetComponentsInChildren<HarvestInkStreamEffect>(true))
            {
                var so = new SerializedObject(effect); so.FindProperty("_sinkAnchor").objectReferenceValue = visual.EffectTip; so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        private static void SetLayer(Transform root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
        private static void EnsureViewmodelCamera(GameObject root, PlayerVisualRig rig)
        {
            var tag = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var entry = tag.FindProperty("layers").GetArrayElementAtIndex(ViewmodelLayer);
            if (!string.IsNullOrEmpty(entry.stringValue) && entry.stringValue != "DosaViewmodel") throw new InvalidOperationException("Viewmodel layer already occupied");
            entry.stringValue = "DosaViewmodel"; tag.ApplyModifiedPropertiesWithoutUndo();
            var main = root.GetComponentsInChildren<Camera>(true).First(c => c.CompareTag("MainCamera"));
            var old = main.transform.Find("DosaArmsCamera");
            var eye = old != null ? old.gameObject : new GameObject("DosaArmsCamera");
            eye.transform.SetParent(main.transform, false); eye.transform.localPosition = Vector3.zero; eye.transform.localRotation = Quaternion.identity;
            Record(eye.transform);
            var overlay = eye.GetComponent<Camera>();
            if (overlay == null) overlay = eye.AddComponent<Camera>();
            overlay.cullingMask = 1 << ViewmodelLayer; overlay.clearFlags = CameraClearFlags.Depth;
            overlay.allowHDR = main.allowHDR; overlay.allowMSAA = main.allowMSAA;
            var data = overlay.GetUniversalAdditionalCameraData(); data.renderType = CameraRenderType.Overlay;
            var overlayData = new SerializedObject(data); overlayData.FindProperty("m_ClearDepth").boolValue = true; overlayData.ApplyModifiedPropertiesWithoutUndo();
            data.renderPostProcessing = false; data.renderShadows = false;
            main.cullingMask &= ~(1 << ViewmodelLayer);
            var mainData = main.GetUniversalAdditionalCameraData();
            mainData.cameraStack.RemoveAll(c => c == null);
            if (!mainData.cameraStack.Contains(overlay)) mainData.cameraStack.Add(overlay);
            rig.ConfigureViewCamera(overlay);
            Record(main); Record(mainData); Record(overlay); Record(data); Record(rig);
        }
        private static void Record(Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }
        public static string AuditAssets()
        {
            var report = new List<string>();
            foreach (var path in new[] { WorldModel, ArmsModel })
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) { report.Add("FAIL missing " + path); continue; }
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                int tris = asset.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => r.sharedMesh.triangles.Length / 3);
                report.Add(Path.GetFileName(path) + " human=" + (avatar != null && avatar.isHuman && avatar.isValid) + " tris=" + tris);
                var bones = asset.GetComponentsInChildren<Transform>(true);
                foreach (string name in new[] { "Hips", "Head", "RightHand", "LeftHand", "RightFoot", "RightToeBase" })
                {
                    var bone = bones.FirstOrDefault(t => t.name == name);
                    if (bone != null) report.Add(name + "=" + asset.transform.InverseTransformPoint(bone.position).ToString("F3"));
                }
            }
            report.Add("clips=" + MotionNames.Count(n => AssetDatabase.LoadAllAssetsAtPath(ClipPath(n)).OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview__"))));
            return string.Join("; ", report);
        }
        public static string AuditScene()
        {
            var scene = SceneManager.GetActiveScene(); var roots = scene.GetRootGameObjects();
            var rigs = roots.SelectMany(r => r.GetComponentsInChildren<PlayerVisualRig>(true)).ToArray();
            var drivers = roots.SelectMany(r => r.GetComponentsInChildren<PlayerVisualDriver>(true)).ToArray();
            int missing = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            int cameras = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Count(c => c.name == "DosaArmsCamera");
            int badRefs = 0;
            foreach (var rig in rigs)
            {
                var eye = new SerializedObject(rig).FindProperty("_viewCamera").objectReferenceValue as Camera;
                var main = rig.transform.root.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.CompareTag("MainCamera"));
                if (eye == null || main == null || !main.GetUniversalAdditionalCameraData().cameraStack.Contains(eye)) badRefs++;
            }
            foreach (var driver in drivers)
                if (new SerializedObject(driver).FindProperty("_rig").objectReferenceValue == null) badRefs++;
            foreach (var effect in roots.SelectMany(r => r.GetComponentsInChildren<HarvestInkStreamEffect>(true)))
                if (new SerializedObject(effect).FindProperty("_sinkAnchor").objectReferenceValue == null) badRefs++;
            return "scene=" + scene.name + "; visuals=" + rigs.Length + "; drivers=" + drivers.Length + "; armsCameras=" + cameras + "; brokenPresentationRefs=" + badRefs + "; missingScripts=" + missing + "; " + AuditAssets();
        }
        private static void RequireEdit() { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first"); }
        private static void RequireSavedScene() { if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save current edits before switching scenes"); }
    }
}
