using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>
    /// New-generation assets only. Imports an authored rest rig and creates an isolated static review scene.
    /// This builder never creates/retargets animation clips or controllers and never publishes to C2/PlayerRig.
    /// A valid Unity Avatar is structural evidence, not the artistic/deformation RIG_PASS gate.
    /// </summary>
    public static class DosaV2PlayerBuilder
    {
        public const string Folder = "Assets/_Project/Art/Characters/DosaV2";
        public const string WorldModel = Folder + "/Models/SM_DosaV2_Rigged.fbx";
        public const string ArmsModel = Folder + "/Models/SM_DosaV2_Arms.fbx";
        public const string BrushModel = Folder + "/Models/SM_DosaBrushV2.fbx";
        public const string WorldLod1 = Folder + "/Models/SM_DosaV2_LOD1.fbx";
        public const string WorldLod2 = Folder + "/Models/SM_DosaV2_LOD2.fbx";
        public const string ValidationPrefab = "Assets/_Project/Prefabs/Player/PF_DosaV2_RigValidation.prefab";
        public const string ValidationScene = "Assets/_Project/Scenes/Dev/C2_PlayerV2Validation.unity";
        public const string BrushProfilePath = "Assets/_Project/Data/PlayerV2/BrushDeformation_DosaV2.asset";
        public const string VisualProfilePath = "Assets/_Project/Data/PlayerV2/PlayerVisual_DosaV2.asset";
        public const string DrawingProfilePath = "Assets/_Project/Data/PlayerV2/DrawingPose_DosaV2.asset";
        public static string StagingDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Staging"));

        [Serializable] public sealed class TextureManifest
        {
            public MaterialEntry[] materials = Array.Empty<MaterialEntry>();
            public RendererMaterialEntry[] rendererMaterials = Array.Empty<RendererMaterialEntry>();
        }
        [Serializable] public sealed class RendererMaterialEntry
        {
            public string renderer;
            public string[] materials = Array.Empty<string>();
        }
        [Serializable] public sealed class MaterialEntry
        {
            public string name, baseColor, normal, metallic, roughness, occlusion;
            public int metallicChannel, roughnessChannel;
            public string[] rendererNames = Array.Empty<string>();
            public float metallicFallback, roughnessFallback = .75f, normalStrength = 1f;
            public Color baseTint = Color.white;
            public bool useSolidColor;
            public bool doubleSided;
        }
        [Serializable] private sealed class CopyRecord { public string source, destination, sha256, previousVersion; }
        [Serializable] private sealed class FingerOffset { public string name; public Quaternion offset; }
        [Serializable] private sealed class FingerCalibration { public FingerOffset[] offsets; }
        [Serializable] private sealed class ModelAudit
        {
            public string path;
            public bool humanoid, validAvatar, hasController;
            public int renderers, vertices, triangles, skeletonTransforms, mappedHumanoidBones, missingSkinBones, invalidWeightVertices;
            public int actualAnimationClips;
            public Vector3 boundsSize;
            public List<string> missingRequiredBones = new List<string>();
            public List<string> materialWarnings = new List<string>();
        }
        [Serializable] private sealed class BuildReport
        {
            public string status, rigGate = "RIG_REVIEW_REQUIRED", secondaryBinding, lodBinding, secondaryCollisionBinding, surfaceAttachmentBinding;
            public string note = "Static newly authored assets only. No production animation, retarget, shared PlayerRig or canonical C2 mutation. Avatar validity does not grant RIG_PASS.";
            public List<string> missing = new List<string>();
            public List<string> warnings = new List<string>();
            public List<CopyRecord> copies = new List<CopyRecord>();
            public List<ModelAudit> models = new List<ModelAudit>();
            public BrushReview brush;
        }
        [Serializable] private sealed class BrushReview
        {
            public bool bound, splayAvailable, fixedLengths;
            public int poses;
            public float restGripToTipMeters, bristleArcMeters, maximumTipErrorMeters, maximumSegmentLengthDeltaMeters;
        }

        [MenuItem("Oheangbu/Player V2/1. Import staged static rig assets")]
        private static void ImportMenu() => Debug.Log(ImportAssets());
        [MenuItem("Oheangbu/Player V2/2. Build isolated static rig review")]
        private static void BuildMenu() => Debug.Log(BuildValidation());

        public static string ImportAssets()
        {
            RequireEdit();
            var report = new BuildReport();
            string[] relative = { "Character/SM_DosaV2_Rigged.fbx", "Character/SM_DosaV2_Arms.fbx", "Brush/SM_DosaBrushV2.fbx", "Character/SM_DosaV2_LOD1.fbx", "Character/SM_DosaV2_LOD2.fbx" };
            string[] target = { WorldModel, ArmsModel, BrushModel, WorldLod1, WorldLod2 };
            for (int i = 0; i < relative.Length; i++) if (!File.Exists(Source(relative[i]))) report.missing.Add(relative[i]);
            if (report.missing.Count > 0) { report.status = "WAIT_FOR_STAGED_MODELS"; return SaveReport(report, "import"); }
            for (int i = 0; i < relative.Length; i++)
            {
                CopyVersioned(Source(relative[i]), target[i], report);
                ImportModel(target[i], target[i] != BrushModel);
            }
            var manifest = ReadManifest(report);
            if (manifest != null) ImportMaterials(manifest, report);
            foreach (string path in target) report.models.Add(Audit(path));
            foreach (var model in report.models)
            {
                int budget = model.path == WorldModel ? 75000 : model.path == ArmsModel ? 26000 : model.path == BrushModel ? 5000 : model.path == WorldLod1 ? 35000 : 18000;
                if (model.triangles > budget) throw new InvalidDataException(model.path + ": actual imported triangle count " + model.triangles + " exceeds " + budget);
            }
            AssetDatabase.SaveAssets();
            bool valid = report.models.All(m => m.missingRequiredBones.Count == 0 && m.missingSkinBones == 0
                && m.invalidWeightVertices == 0 && m.actualAnimationClips == 0 && !m.hasController
                && (m.path == BrushModel || (m.humanoid && m.validAvatar)));
            report.status = valid ? (manifest == null ? "STATIC_IMPORT_READY_MATERIALS_PENDING" : "STATIC_IMPORT_READY") : "RIG_STRUCTURE_REVIEW_FAILED";
            return SaveReport(report, "import");
        }

        /// <summary>New version only; preserves existing authored V2 tuning on later imports.</summary>
        public static string BuildPresentationProfiles()
        {
            RequireEdit(); EnsureFolder("Assets/_Project/Data/PlayerV2");
            var visual = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(VisualProfilePath);
            if (visual == null)
            {
                var original = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>("Assets/_Project/Data/Player/PlayerVisual_Dosa.asset");
                if (original == null) throw new InvalidDataException("Missing original visual profile.");
                visual = Object.Instantiate(original); visual.name = "PlayerVisual_DosaV2";
                visual.Controller = null; visual.ModelHeight = 1.75f; visual.RestTipDepth = 1.05f;
                AssetDatabase.CreateAsset(visual, VisualProfilePath);
            }
            var drawing = AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>(DrawingProfilePath);
            if (drawing == null)
            {
                var original = AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>("Assets/_Project/Data/Player/DrawingPose_Dosa.asset");
                if (original == null) throw new InvalidDataException("Missing original drawing profile.");
                drawing = Object.Instantiate(original); drawing.name = "DrawingPose_DosaV2";
                drawing.BrushLength = .743781624f; drawing.PreferredTipDepth = 1.05f;
                drawing.MinTipDepth = .82f; drawing.MaxTipDepth = 1.65f; drawing.WorldPlaneDistance = 1.02f;
                string calibrationPath = Path.GetFullPath(Path.Combine(StagingDirectory, "../Calibration/unity-grip-offsets.json"));
                var calibration = JsonUtility.FromJson<FingerCalibration>(File.ReadAllText(calibrationPath));
                string[] names = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }.SelectMany(f => Enumerable.Range(1, 3).Select(n => "RightHand" + f + n)).ToArray();
                var right = calibration.offsets.Where(o => o.name.StartsWith("RightHand", StringComparison.Ordinal)).ToArray();
                if (right.Length != 15 || !right.Select(o => o.name).SequenceEqual(names)) throw new InvalidDataException("Incomplete V2 grip calibration.");
                drawing.CalibratedRightFingerOffsets = right.Select(o => o.offset).ToArray();
                AssetDatabase.CreateAsset(drawing, DrawingProfilePath);
            }
            // Explicitly opt in only this isolated V2 profile. Other authored fields remain intact.
            if (!drawing.EnableArmRollRedistribution)
            {
                drawing.EnableArmRollRedistribution = true;
                EditorUtility.SetDirty(drawing);
            }
            AssetDatabase.SaveAssets();
            return "V2_PROFILES_READY: calibrated long-brush presentation; controller remains empty until RIG_PASS.";
        }

        /// <summary>Allows the independently completed brush to be inspected while the character is still being authored.</summary>
        public static string ImportBrushAssets()
        {
            RequireEdit();
            var report = new BuildReport();
            string source = Source("Brush/SM_DosaBrushV2.fbx");
            if (!File.Exists(source)) { report.status = "WAIT_FOR_STAGED_BRUSH"; report.missing.Add(source); return SaveReport(report, "brush-static-rig"); }
            CopyVersioned(source, BrushModel, report); ImportModel(BrushModel, false);
            report.models.Add(Audit(BrushModel));
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel), scene);
                BindBrush(instance, report);
                var rig = instance.GetComponent<BrushBristleRig>();
                report.brush = new BrushReview { bound = rig != null && rig.IsBound };
                if (report.brush.bound)
                {
                    var transforms = instance.GetComponentsInChildren<Transform>(true);
                    var grip = transforms.First(t => t.name == "GripSocket");
                    var chain = Enumerable.Range(1, 6).Select(i => transforms.First(t => t.name == "Bristle_" + i.ToString("00"))).ToArray();
                    var lengths = new float[6];
                    for (int i = 0; i < 6; i++) lengths[i] = Vector3.Distance(chain[i].position, i < 5 ? chain[i + 1].position : rig.TipSocket.position);
                    report.brush.restGripToTipMeters = Vector3.Distance(grip.position, rig.TipSocket.position);
                    report.brush.splayAvailable = rig.SplayAvailable;
                    foreach (Vector3 velocity in new[] { Vector3.zero, Vector3.right * 2f, Vector3.forward * 2f, -Vector3.right * 2f })
                    foreach (bool stroking in new[] { true, false })
                    {
                        var pose = rig.EvaluateLocal(velocity, stroking, 1f / 60f);
                        var targetTip = new Vector3(.2f, 1.1f, .3f);
                        Quaternion rotation = Quaternion.Euler(24f, -37f, 12f);
                        instance.transform.SetPositionAndRotation(targetTip - rotation * pose.TipPositionLocal, rotation);
                        rig.ApplyPose(); report.brush.poses++;
                        report.brush.bristleArcMeters = pose.BristleArcLength;
                        report.brush.maximumTipErrorMeters = Mathf.Max(report.brush.maximumTipErrorMeters, Vector3.Distance(rig.ActualTipWorld, targetTip));
                        for (int i = 0; i < 6; i++) report.brush.maximumSegmentLengthDeltaMeters = Mathf.Max(report.brush.maximumSegmentLengthDeltaMeters,
                            Mathf.Abs(Vector3.Distance(chain[i].position, i < 5 ? chain[i + 1].position : rig.TipSocket.position) - lengths[i]));
                    }
                    report.brush.fixedLengths = report.brush.maximumSegmentLengthDeltaMeters <= .0001f && report.brush.maximumTipErrorMeters <= .0001f;
                }
                report.status = report.brush.bound && report.brush.splayAvailable && report.brush.fixedLengths
                    ? "BRUSH_STATIC_IMPORT_READY" : "BRUSH_STRUCTURE_REVIEW_FAILED";
                AssetDatabase.SaveAssets(); return SaveReport(report, "brush-static-rig");
            }
            finally { if (instance != null) Object.DestroyImmediate(instance); EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static string BuildValidation()
            => BuildValidationInternal(true);
        public static string BuildCorePreview()
        {
            try { return BuildValidationInternal(false); }
            catch (Exception e) { return Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "CORE_PREVIEW_BUILD_FAILED", error = e.GetBaseException().Message }); }
        }
        private static string BuildValidationInternal(bool includeLods)
        {
            RequireEdit();
            var report = new BuildReport();
            foreach (string path in new[] { WorldModel, ArmsModel, BrushModel })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) report.missing.Add(path);
            if (report.missing.Count > 0) { report.status = "WAIT_FOR_IMPORT"; return SaveReport(report, "static-rig"); }
            if (SceneManager.GetSceneByPath(ValidationScene).isLoaded)
                return "NOT_RUN: close the existing V2 validation scene before rebuilding its generated asset.";
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject("DosaV2_RigValidation");
                var world = InstantiateModel(WorldModel, "DosaV2_World_Rest", root.transform, new Vector3(-.9f, 0f, 0f));
                var near = InstantiateModel(ArmsModel, "DosaV2_NearArms_Rest", root.transform, new Vector3(1f, 0f, 0f));
                var brush = InstantiateModel(BrushModel, "DosaV2_Brush_Rest", root.transform, new Vector3(0f, .25f, -.4f));
                foreach (var renderer in near.GetComponentsInChildren<Renderer>(true)) renderer.shadowCastingMode = ShadowCastingMode.Off;
                var manifest = ReadManifest(report);
                if (manifest != null) { ApplyMaterials(world, manifest, report); ApplyMaterials(near, manifest, report); ApplyMaterials(brush, manifest, report); }
                else ApplyNeutralReviewMaterial(root);
                BindBrush(brush, report);
                var nearReference = new GameObject("Near representation camera reference").AddComponent<Camera>();
                nearReference.transform.SetParent(root.transform, false);
                nearReference.transform.position = new Vector3(1f, 1.58f, -.25f);
                nearReference.enabled = false;
                report.secondaryBinding = DosaV2SecondaryBuilder.Bind(world, near, nearReference, brush, includeLods);
                var secondaryReport = Newtonsoft.Json.Linq.JObject.Parse(report.secondaryBinding);
                if ((string)secondaryReport["status"] != "BOUND_STATIC")
                    throw new InvalidOperationException("Secondary authoring failed: " + (string)secondaryReport["reason"]);
                if (world.GetComponentsInChildren<Transform>(true).Any(t => t.name == "J_WaistSideBottleAnchor_L"))
                {
                    report.surfaceAttachmentBinding = Oheangbu.Editor.DosaV2SurfaceAttachmentBuilder.Bind(world);
                    string attachmentOutput = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/surface-attachment-binding.json"));
                    Directory.CreateDirectory(Path.GetDirectoryName(attachmentOutput));
                    File.WriteAllText(attachmentOutput, report.surfaceAttachmentBinding);
                    var attachmentReport = Newtonsoft.Json.Linq.JObject.Parse(report.surfaceAttachmentBinding);
                    if ((string)attachmentReport["status"] != "BOUND_STATIC_PENDING_PLAY")
                        throw new InvalidOperationException("Surface attachment authoring failed: " + (string)attachmentReport["error"]);
                    var secondary = world.GetComponent<PlayerSecondaryMotionRig>();
                    if (secondary == null || !secondary.ConfigureSurfaceAttachments(world.GetComponent<PlayerSkinnedSurfaceAttachmentRig>()))
                        throw new InvalidOperationException("Surface attachment spring integration failed: " + secondary?.LastBindingError);
                }
                report.lodBinding = includeLods ? DosaV2LodBuilder.Bind(world, nearReference) : "{\"status\":\"DEFERRED_CORE_PREVIEW\"}";
                report.secondaryCollisionBinding = DosaV2SecondaryCollisionBuilder.Bind(world);
                var collisionReport = Newtonsoft.Json.Linq.JObject.Parse(report.secondaryCollisionBinding);
                if ((string)collisionReport["status"] != "BOUND_AWAITING_ACTUAL_COLLISION_REPLAY")
                    throw new InvalidOperationException("Secondary collision authoring failed: " + (string)collisionReport["error"]);
                EnsureFolder(Path.GetDirectoryName(ValidationPrefab));
                PrefabUtility.SaveAsPrefabAsset(root, ValidationPrefab);
                CreateReviewEnvironment();
                EnsureFolder(Path.GetDirectoryName(ValidationScene));
                if (!EditorSceneManager.SaveScene(scene, ValidationScene)) throw new IOException("Could not save isolated V2 rig validation scene.");
                foreach (string path in new[] { WorldModel, ArmsModel, BrushModel }) report.models.Add(Audit(path));
                report.status = includeLods ? "STATIC_REVIEW_READY" : "CORE_PREVIEW_READY_LODS_DEFERRED";
                report.warnings.Add("Inspect hands, shoulders, elbows, clothing seams and ornaments with manual poses before granting RIG_PASS. The scene contains no movement/animation controller.");
                AssetDatabase.SaveAssets();
                return SaveReport(report, includeLods ? "static-rig" : "core-static-rig");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        /// <summary>Reserved integration hook: no coefficient guessing, automatic collider creation or pre-gate simulation.</summary>
        public static string BindSecondaryAssets(GameObject world, GameObject near)
        {
            if (world == null || near == null) return "WAIT: both newly authored representations are required.";
            return "WAIT: provide the explicit camera reference to DosaV2SecondaryBuilder.Bind(world, near, camera).";
        }

        private static void ImportModel(string path, bool humanoid)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing model importer: " + path);
            importer.globalScale = 1f; importer.useFileScale = true;
            importer.importCameras = false; importer.importLights = false; importer.importVisibility = false;
            importer.importAnimation = false; importer.optimizeGameObjects = false; importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importBlendShapes = true; importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapeNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            importer.avatarSetup = humanoid ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
            if (humanoid) importer.humanDescription = HumanDescriptionFor(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            importer.SaveAndReimport();
        }

        public static HumanDescription HumanDescriptionFor(GameObject model)
        {
            if (model == null) throw new InvalidOperationException("Import the new rest-pose FBX before building its HumanDescription.");
            var bones = model.GetComponentsInChildren<Transform>(true);
            var normalized = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var bone in bones)
            {
                string name = BoneName(bone.name);
                if (normalized.ContainsKey(name)) throw new InvalidOperationException("Ambiguous normalized bone name: " + name);
                normalized.Add(name, bone);
            }
            var map = new Dictionary<HumanBodyBones, string[]>
            {
                { HumanBodyBones.Hips, new[] { "Hips" } }, { HumanBodyBones.Spine, new[] { "Spine" } },
                { HumanBodyBones.Chest, new[] { "Spine01", "Spine1" } }, { HumanBodyBones.UpperChest, new[] { "Spine02", "Spine2" } },
                { HumanBodyBones.Neck, new[] { "Neck" } }, { HumanBodyBones.Head, new[] { "Head" } },
                { HumanBodyBones.LeftShoulder, new[] { "LeftShoulder" } }, { HumanBodyBones.RightShoulder, new[] { "RightShoulder" } },
                { HumanBodyBones.LeftUpperArm, new[] { "LeftArm" } }, { HumanBodyBones.RightUpperArm, new[] { "RightArm" } },
                { HumanBodyBones.LeftLowerArm, new[] { "LeftForeArm" } }, { HumanBodyBones.RightLowerArm, new[] { "RightForeArm" } },
                { HumanBodyBones.LeftHand, new[] { "LeftHand" } }, { HumanBodyBones.RightHand, new[] { "RightHand" } },
                { HumanBodyBones.LeftUpperLeg, new[] { "LeftUpLeg" } }, { HumanBodyBones.RightUpperLeg, new[] { "RightUpLeg" } },
                { HumanBodyBones.LeftLowerLeg, new[] { "LeftLeg" } }, { HumanBodyBones.RightLowerLeg, new[] { "RightLeg" } },
                { HumanBodyBones.LeftFoot, new[] { "LeftFoot" } }, { HumanBodyBones.RightFoot, new[] { "RightFoot" } },
                { HumanBodyBones.LeftToes, new[] { "LeftToeBase" } }, { HumanBodyBones.RightToes, new[] { "RightToeBase" } }
            };
            foreach (string side in new[] { "Left", "Right" })
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                int index = 1;
                foreach (string joint in new[] { "Proximal", "Intermediate", "Distal" })
                    map[(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + joint)]
                        = new[] { side + "Hand" + (finger == "Little" ? "Pinky" : finger) + index++ };
            }
            var human = new List<HumanBone>();
            foreach (var entry in map)
                foreach (string name in entry.Value)
                    if (normalized.TryGetValue(name, out var bone))
                    {
                        human.Add(new HumanBone { humanName = HumanTrait.BoneName[(int)entry.Key], boneName = bone.name,
                            limit = new HumanLimit { useDefaultValues = true } });
                        break;
                    }
            return new HumanDescription
            {
                human = human.ToArray(),
                skeleton = bones.Select(b => new SkeletonBone { name = b.name, position = b.localPosition, rotation = b.localRotation, scale = b.localScale }).ToArray(),
                armStretch = 0f, legStretch = 0f, upperArmTwist = .5f, lowerArmTwist = .5f,
                upperLegTwist = .5f, lowerLegTwist = .5f, feetSpacing = 0f, hasTranslationDoF = false
            };
        }

        private static string BoneName(string name)
        { int separator = Math.Max(name.LastIndexOf(':'), name.LastIndexOf('|')); return separator < 0 ? name : name.Substring(separator + 1); }

        private static TextureManifest ReadManifest(BuildReport report)
        {
            string path = Source("texture-manifest.json");
            if (!File.Exists(path)) { report.warnings.Add("texture-manifest.json is absent; no unrelated UV atlas will be assigned."); return null; }
            var manifest = JsonUtility.FromJson<TextureManifest>(File.ReadAllText(path));
            if (manifest == null || manifest.materials == null || manifest.materials.Length == 0)
                throw new InvalidDataException("texture-manifest.json requires a nonempty materials array.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var material in manifest.materials)
            {
                if (material == null || string.IsNullOrWhiteSpace(material.name) || SafeName(material.name) != material.name || !names.Add(material.name))
                    throw new InvalidDataException("Material names must be unique filename-safe identifiers.");
                if (string.IsNullOrEmpty(material.baseColor) && !material.useSolidColor)
                    throw new InvalidDataException(material.name + " requires its own baseColor source or an explicit useSolidColor declaration.");
                foreach (string file in new[] { material.baseColor, material.normal, material.metallic, material.roughness, material.occlusion })
                    if (!string.IsNullOrEmpty(file) && !File.Exists(Source(file))) throw new FileNotFoundException("Missing declared texture", file);
                if (material.metallicChannel < 0 || material.metallicChannel > 3 || material.roughnessChannel < 0 || material.roughnessChannel > 3)
                    throw new InvalidDataException("Data texture channels must be 0=R, 1=G, 2=B, 3=A.");
            }
            return manifest;
        }

        private static void ImportMaterials(TextureManifest manifest, BuildReport report)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit is unavailable.");
            EnsureFolder(Folder + "/Materials");
            foreach (var entry in manifest.materials)
            {
                string materialPath = Folder + "/Materials/M_" + entry.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(shader) { name = "M_" + entry.name }; AssetDatabase.CreateAsset(material, materialPath); }
                material.shader = shader;
                material.SetTexture("_BaseMap", ImportTexture(entry.baseColor, entry.name + "_BaseColor", true, false, 4096, report));
                material.SetColor("_BaseColor", entry.baseTint);
                var normal = ImportTexture(entry.normal, entry.name + "_Normal", false, true, 2048, report);
                material.SetTexture("_BumpMap", normal); Keyword(material, "_NORMALMAP", normal != null); material.SetFloat("_BumpScale", entry.normalStrength);
                var packed = PackMetallicSmoothness(entry, report);
                material.SetTexture("_MetallicGlossMap", packed); Keyword(material, "_METALLICSPECGLOSSMAP", packed != null);
                material.SetFloat("_Metallic", Mathf.Clamp01(entry.metallicFallback));
                material.SetFloat("_Smoothness", packed != null ? 1f : 1f - Mathf.Clamp01(entry.roughnessFallback));
                material.SetFloat("_SmoothnessTextureChannel", 0f); material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                var ao = ImportTexture(entry.occlusion, entry.name + "_Occlusion", false, false, 2048, report);
                material.SetTexture("_OcclusionMap", ao); Keyword(material, "_OCCLUSIONMAP", ao != null); material.SetFloat("_OcclusionStrength", 1f);
                material.SetFloat("_Surface", 0f); material.SetFloat("_AlphaClip", 0f); material.SetFloat("_Cull", entry.doubleSided ? 0f : 2f);
                material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetColor("_EmissionColor", Color.black); material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
            }
        }

        private static Texture2D ImportTexture(string relative, string name, bool srgb, bool normal, int maximumSize, BuildReport report)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            string extension = Path.GetExtension(relative).ToLowerInvariant();
            string path = Folder + "/Textures/T_" + name + extension;
            CopyVersioned(Source(relative), path, report);
            ConfigureTexture(path, srgb, normal, maximumSize);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D PackMetallicSmoothness(MaterialEntry entry, BuildReport report)
        {
            if (string.IsNullOrEmpty(entry.metallic) && string.IsNullOrEmpty(entry.roughness)) return null;
            Texture2D metallic = null, roughness = null, packed = null;
            try
            {
                metallic = ReadRawDataTexture(entry.metallic); roughness = ReadRawDataTexture(entry.roughness);
                var reference = metallic != null ? metallic : roughness;
                if (metallic != null && roughness != null && (metallic.width != roughness.width || metallic.height != roughness.height))
                    throw new InvalidDataException(entry.name + ": metallic and roughness dimensions differ; author matching maps before packing.");
                var m = metallic != null ? metallic.GetPixels32() : null;
                var r = roughness != null ? roughness.GetPixels32() : null;
                var pixels = new Color32[reference.width * reference.height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(
                    m != null ? Channel(m[i], entry.metallicChannel) : (byte)Mathf.RoundToInt(Mathf.Clamp01(entry.metallicFallback) * 255f), 0, 0,
                    (byte)(255 - (r != null ? Channel(r[i], entry.roughnessChannel) : Mathf.RoundToInt(Mathf.Clamp01(entry.roughnessFallback) * 255f))));
                packed = new Texture2D(reference.width, reference.height, TextureFormat.RGBA32, false, true);
                packed.SetPixels32(pixels); packed.Apply(false, false);
                string path = Folder + "/Textures/T_" + entry.name + "_MetallicSmoothness.png";
                WriteVersioned(ImageConversion.EncodeToPNG(packed), path, "packed data: metallic→R, 1−roughness→A", report);
                ConfigureTexture(path, false, false, 2048);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            finally { if (metallic != null) Object.DestroyImmediate(metallic); if (roughness != null) Object.DestroyImmediate(roughness); if (packed != null) Object.DestroyImmediate(packed); }
        }

        private static Texture2D ReadRawDataTexture(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (ImageConversion.LoadImage(texture, File.ReadAllBytes(Source(relative)), false)) return texture;
            Object.DestroyImmediate(texture); throw new InvalidDataException("Could not decode data texture: " + relative);
        }
        private static byte Channel(Color32 color, int index) => index == 0 ? color.r : index == 1 ? color.g : index == 2 ? color.b : color.a;
        private static void ConfigureTexture(string path, bool srgb, bool normal, int maximumSize)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing texture importer: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb; importer.maxTextureSize = maximumSize; importer.mipmapEnabled = true;
            importer.isReadable = false; importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }

        private static GameObject InstantiateModel(string path, string name, Transform parent, Vector3 position)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
            instance.name = name; instance.transform.localPosition = position;
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
            { animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            return instance;
        }
        private static void ApplyMaterials(GameObject root, TextureManifest manifest, BuildReport report)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var explicitSlots = (manifest.rendererMaterials ?? Array.Empty<RendererMaterialEntry>())
                    .Where(m => string.Equals(m.renderer, renderer.name, StringComparison.Ordinal)).ToArray();
                if (explicitSlots.Length == 1)
                {
                    var names = explicitSlots[0].materials ?? Array.Empty<string>();
                    var slotMesh = renderer is SkinnedMeshRenderer slotSkin ? slotSkin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    int submeshCount = slotMesh != null ? slotMesh.subMeshCount : renderer.sharedMaterials.Length;
                    if (names.Length != submeshCount)
                        throw new InvalidDataException(renderer.name + ": authored material slot count " + names.Length + " differs from imported submeshes " + submeshCount + ".");
                    renderer.sharedMaterials = names.Select(name => AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/M_" + name + ".mat")
                        ?? throw new InvalidDataException("Missing authored material " + name)).ToArray();
                    continue;
                }
                if (explicitSlots.Length > 1) throw new InvalidDataException("Duplicate renderer material mapping: " + renderer.name);
                var matches = manifest.materials.Where(m => (m.rendererNames ?? Array.Empty<string>()).Contains(renderer.name, StringComparer.Ordinal)).ToArray();
                if (matches.Length != 1) { report.warnings.Add(renderer.name + ": expected exactly one explicit UV/material mapping; assigned neutral review material."); renderer.sharedMaterial = NeutralMaterial(); continue; }
                var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/M_" + matches[0].name + ".mat");
                if (material == null) throw new InvalidOperationException("Run ImportAssets before building mapped materials.");
                var slots = renderer.sharedMaterials;
                if (slots.Length != 1) { report.warnings.Add(renderer.name + ": multiple material slots require an explicit per-slot manifest before visual approval."); continue; }
                renderer.sharedMaterial = material;
            }
        }
        private static void BindBrush(GameObject brush, BuildReport report)
        {
            var transforms = brush.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => transforms.FirstOrDefault(t => t.name == name);
            var bones = Enumerable.Range(1, 6).Select(i => Find("Bristle_" + i.ToString("00"))).ToArray();
            var grip = Find("GripSocket"); var tip = Find("TipSocket");
            var skin = brush.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "DosaBrushV2_Bristles");
            if (grip == null || tip == null || bones.Any(b => b == null) || skin == null)
            { report.warnings.Add("Brush authored sockets, six-bone chain or bristle skin are incomplete; no inferred binding was installed."); return; }
            var profile = AssetDatabase.LoadAssetAtPath<BrushDeformationProfileSO>(BrushProfilePath);
            if (profile == null)
            {
                EnsureFolder(Path.GetDirectoryName(BrushProfilePath)); profile = ScriptableObject.CreateInstance<BrushDeformationProfileSO>();
                profile.NominalTotalLength = .9f; profile.NominalShaftLength = .6416083f;
                AssetDatabase.CreateAsset(profile, BrushProfilePath);
            }
            var rig = brush.AddComponent<BrushBristleRig>();
            if (!rig.Configure(profile, grip, tip, bones, skin)) report.warnings.Add("Brush rest binding: " + rig.BindingError);
            else if (!rig.SplayAvailable) report.warnings.Add("Brush is missing the authored BristleSplay blendshape.");
        }

        private static ModelAudit Audit(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var result = new ModelAudit { path = path };
            result.actualAnimationClips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene); instance.hideFlags = HideFlags.HideAndDontSave;
                result.skeletonTransforms = instance.GetComponentsInChildren<Transform>(true).Length;
                var animator = instance.GetComponentInChildren<Animator>(true);
                result.hasController = animator != null && animator.runtimeAnimatorController != null;
                result.humanoid = animator != null && animator.isHuman;
                result.validAvatar = animator != null && animator.avatar != null && animator.avatar.isValid;
                if (path != BrushModel)
                {
                    if (!result.humanoid || !result.validAvatar) result.missingRequiredBones.Add("Valid Humanoid Avatar");
                    else for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    {
                        var bone = animator.GetBoneTransform((HumanBodyBones)i);
                        if (bone != null) result.mappedHumanoidBones++;
                        else if (HumanTrait.RequiredBone(i) || ((HumanBodyBones)i >= HumanBodyBones.LeftThumbProximal && (HumanBodyBones)i <= HumanBodyBones.RightLittleDistal))
                            result.missingRequiredBones.Add(((HumanBodyBones)i).ToString());
                    }
                }
                var renderers = instance.GetComponentsInChildren<Renderer>(true); result.renderers = renderers.Length;
                Bounds bounds = new Bounds(); bool hasBounds = false;
                foreach (var renderer in renderers)
                {
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds);
                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        mesh = skin.sharedMesh; result.missingSkinBones += skin.bones.Count(b => b == null);
                        if (mesh != null) foreach (var weight in mesh.boneWeights)
                        {
                            float total = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
                            if (!float.IsFinite(total) || weight.weight0 < 0f || weight.weight1 < 0f || weight.weight2 < 0f || weight.weight3 < 0f
                                || Mathf.Abs(total - 1f) > .0001f) result.invalidWeightVertices++;
                        }
                    }
                    else { var filter = renderer.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
                    if (mesh != null) { result.vertices += mesh.vertexCount; result.triangles += mesh.triangles.Length / 3; }
                    if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name.Contains("InternalErrorShader"))) result.materialWarnings.Add(renderer.name);
                }
                result.boundsSize = bounds.size;
                return result;
            }
            finally { if (instance != null) Object.DestroyImmediate(instance); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void CreateReviewEnvironment()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Static review floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>()); floor.transform.localScale = Vector3.one * .7f;
            floor.GetComponent<Renderer>().sharedMaterial = NeutralMaterial();
            var light = new GameObject("Neutral review key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f); light.shadows = LightShadows.Soft;
            var fill = new GameObject("Neutral review fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = .6f;
            fill.transform.rotation = Quaternion.Euler(30f, 145f, 0f);
            var camera = new GameObject("Rig review camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(3.5f, 1.9f, -5.5f); camera.transform.LookAt(new Vector3(0f, .95f, 0f));
            camera.fieldOfView = 42f; camera.nearClipPlane = .03f; camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .17f, .18f);
        }
        private static void ApplyNeutralReviewMaterial(GameObject root)
        { foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = Enumerable.Repeat(NeutralMaterial(), Math.Max(1, renderer.sharedMaterials.Length)).ToArray(); }
        private static Material NeutralMaterial()
        {
            string path = Folder + "/Materials/M_DosaV2_NeutralReview.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit missing");
            material = new Material(shader) { name = "M_DosaV2_NeutralReview" };
            material.SetColor("_BaseColor", new Color(.45f, .45f, .43f)); material.SetFloat("_Smoothness", .2f);
            EnsureFolder(Folder + "/Materials"); AssetDatabase.CreateAsset(material, path); return material;
        }
        private static void Keyword(Material material, string keyword, bool enabled)
        { if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword); }

        private static string Source(string relative)
        {
            string root = StagingDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staging source escapes its declared root: " + relative);
            return path;
        }
        private static void CopyVersioned(string source, string destination, BuildReport report)
        { WriteVersioned(File.ReadAllBytes(source), destination, source, report); }
        private static void WriteVersioned(byte[] bytes, string destination, string source, BuildReport report)
        {
            string hash = Hash(bytes), previous = null;
            if (File.Exists(destination))
            {
                byte[] old = File.ReadAllBytes(destination); string oldHash = Hash(old);
                if (oldHash == hash) return;
                string archive = Path.GetFullPath(Path.Combine(StagingDirectory, "../ImportHistory", oldHash));
                Directory.CreateDirectory(archive); previous = Path.Combine(archive, Path.GetFileName(destination));
                if (!File.Exists(previous)) File.WriteAllBytes(previous, old);
            }
            EnsureFolder(Path.GetDirectoryName(destination));
            File.WriteAllBytes(destination, bytes);
            report.copies.Add(new CopyRecord { source = source, destination = destination, sha256 = hash, previousVersion = previous });
        }
        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string SafeName(string name)
        { return string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')); }
        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new InvalidDataException("Asset folder must stay beneath Assets.");
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static string SaveReport(BuildReport report, string name)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2"));
            Directory.CreateDirectory(directory); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, name + "-build-report.json"), json); return json;
        }
        private static void RequireEdit()
        { if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before importing or building the isolated V2 rig scene."); }
    }
}
