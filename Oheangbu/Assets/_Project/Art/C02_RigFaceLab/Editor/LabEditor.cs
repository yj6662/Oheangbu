using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.C02RigFaceLab.Editor
{
    public static class LabEditor
    {
        public const string AssetRoot = "Assets/_Project/Art/C02_RigFaceLab";
        public static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerPhase1/C02_RigFaceLab/Unity"));
        [Serializable] public class ImportRequest
        {
            public string sourceFbx;
            public string name = "EarlyFace";
            public string[] faceChannels = Array.Empty<string>();
            public TextureSource[] textures = Array.Empty<TextureSource>();
            public ClipSource[] clips = Array.Empty<ClipSource>();
            public bool buildScene = true;
            public int maximumInfluences = 4;
        }
        [Serializable] public class TextureSource { public string material; public string baseColor; public string roughness; public string metallic; public string normal; }
        [Serializable] public class ClipSource { public string state; public string take; }
        [Serializable] class ProbeReport { public string unityVersion; public string project; public bool playing; public bool compiling; public string[] scenes; public bool[] sceneDirty; public string scope = "Independent C02 experiment; no canonical scene writes"; }
        [Serializable] class ImportReport
        {
            public string asset; public string source; public string sha256; public string unityVersion;
            public string scope = "Import and numeric technical checks only; artistic deformation and runtime remain UNVERIFIED until separately measured";
            public string animationType; public bool avatarValid; public bool avatarHuman; public bool importBlendShapes; public bool optimizeGameObjects;
            public string animationCompression; public string meshCompression; public float globalScale; public string skinWeightsMode; public int maximumImportedInfluences; public string projectSkinWeights; public float minimumImportedWeight; public bool weldVertices; public bool optimizeMeshVertices;
            public int renderedTriangles; public int vertices; public int rendererCount; public int materialSlots; public int uniqueDeformBones; public int blendShapeCount;
            public string[] bones; public string[] transforms; public RendererReport[] renderers; public ClipReport[] clips;
        }
        [Serializable] class RendererReport { public string path; public string mesh; public int vertices; public int triangles; public int submeshes; public string[] bones; public ShapeReport[] shapes; public Vector3 boundsCenter; public Vector3 boundsSize; public int unweightedVertices; public int nonfiniteWeights; public float maxWeightSumError; public int maxInfluences; public int verticesAboveFourWeights; public string[] materials; }
        [Serializable] class ShapeReport { public string name; public float[] frameWeights; public int affectedVerticesAtLastFrame; public float maximumDelta; }
        [Serializable] class ClipReport { public string name; public float length; public float frameRate; public bool humanMotion; public string[] blendShapeCurves; }
        [Serializable] class BuildReport { public string scene; public string prefab; public string controller; public string profile; public string[] states; public string[] missingRequestedClips; public string[] removedFacialCurvesFromExperimentCopies; public bool canonicalModified = false; public string runtimeStatus = "UNVERIFIED"; public string cameraReference; }
        [Serializable] class FaceTestReport { public string status; public string scope = "Imported BlendShape names, 0..1→0..100 transfer, finite CPU BakeMesh, neutral mesh return. Shape aesthetics, eye contact, runtime coexistence UNVERIFIED."; public string asset; public FaceSample[] samples; public float neutralMaximumDelta; }
        [Serializable] class FaceSample { public string renderer; public string channel; public float normalizedInput; public float unityWeight; public bool finite; public float maximumDeltaFromNeutral; }

        static void EnsureFolders()
        {
            foreach (string folder in new[] { "Models", "Textures", "Materials", "Profiles", "Controllers", "Animations", "Prefabs", "Scenes" }) Directory.CreateDirectory(Path.Combine(Application.dataPath, "_Project/Art/C02_RigFaceLab", folder));
            Directory.CreateDirectory(OutputRoot);
        }
        static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..")) throw new ArgumentException("Invalid experiment file name");
            return value;
        }
        static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string SaveJson(string filename, object report) { EnsureFolders(); string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(OutputRoot, filename), json); return json; }
        public static string Probe()
        {
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            return SaveJson("connection_probe.json", new ProbeReport { unityVersion = Application.unityVersion, project = Application.dataPath, playing = EditorApplication.isPlaying, compiling = EditorApplication.isCompiling, scenes = scenes.Select(s => s.path).ToArray(), sceneDirty = scenes.Select(s => s.isDirty).ToArray() });
        }
        static string Relative(Transform child, Transform parent) => AnimationUtility.CalculateTransformPath(child, parent);
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));

        public static string Import(string request)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop experiment play mode before importing.");
            var data = JsonUtility.FromJson<ImportRequest>(request);
            EnsureFolders();
            string name = SafeName(data.name);
            string source = Path.GetFullPath(data.sourceFbx);
            string allowedSource = Path.GetFullPath(Path.Combine(OutputRoot, "..")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!source.StartsWith(allowedSource, StringComparison.OrdinalIgnoreCase) || !File.Exists(source)) throw new InvalidOperationException("Source FBX must exist under C02_RigFaceLab.");
            string asset = AssetRoot + "/Models/" + name + ".fbx";
            string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", asset));
            File.Copy(source, destination, true);
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(asset);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importBlendShapes = true;
            importer.importAnimation = true;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = Mathf.Clamp(data.maximumInfluences, 1, 255);
            importer.minBoneWeight = 0f;
            importer.optimizeGameObjects = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapeNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            ApplyMaterials(asset, name, data.textures);
            string result = Inspect(asset, source);
            File.WriteAllText(Path.Combine(OutputRoot, name + "_import_request.json"), request);
            if (data.faceChannels.Length > 0) FaceNumericTest(asset, data.faceChannels, name);
            if (data.buildScene) Build(asset, data);
            AssetDatabase.SaveAssets();
            return result;
        }

        public static string InspectAsset(string request) { if (!request.StartsWith(AssetRoot + "/", StringComparison.Ordinal)) throw new ArgumentException("Experiment assets only"); return Inspect(request, null); }
        static string Inspect(string asset, string source)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            if (prefab == null) throw new InvalidOperationException("Model import produced no GameObject: " + asset);
            var importer = (ModelImporter)AssetImporter.GetAtPath(asset);
            var animator = prefab.GetComponent<Animator>();
            var rendererReports = new List<RendererReport>(); var allBones = new HashSet<string>();
            int triangleTotal = 0, vertexTotal = 0, materialTotal = 0, shapeTotal = 0;
            foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh; if (mesh == null) continue;
                int triangles = 0; for (int s = 0; s < mesh.subMeshCount; ++s) { if (mesh.GetTopology(s) != MeshTopology.Triangles) throw new InvalidOperationException("Non-triangle topology"); triangles += (int)mesh.GetIndexCount(s) / 3; }
                var boneNames = renderer.bones.Select(b => b == null ? "<NULL>" : Relative(b, prefab.transform)).ToArray(); foreach (string bone in boneNames) allBones.Add(bone);
                var shapes = new List<ShapeReport>();
                for (int shape = 0; shape < mesh.blendShapeCount; ++shape)
                {
                    int count = mesh.GetBlendShapeFrameCount(shape); var frameWeights = new float[count];
                    for (int f = 0; f < count; ++f) frameWeights[f] = mesh.GetBlendShapeFrameWeight(shape, f);
                    var dv = new Vector3[mesh.vertexCount]; mesh.GetBlendShapeFrameVertices(shape, count - 1, dv, null, null);
                    shapes.Add(new ShapeReport { name = mesh.GetBlendShapeName(shape), frameWeights = frameWeights, affectedVerticesAtLastFrame = dv.Count(v => v.sqrMagnitude > 1e-14f), maximumDelta = dv.Length == 0 ? 0 : dv.Max(v => v.magnitude) });
                }
                var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
                int offset = 0, unweighted = 0, nonfinite = 0, maximumInfluences = 0, aboveFour = 0; float sumError = 0;
                for (int v = 0; v < counts.Length; ++v)
                {
                    int count = counts[v]; if (count == 0) unweighted++; if (count > 4) aboveFour++; maximumInfluences = Math.Max(maximumInfluences, count); float sum = 0;
                    for (int w = 0; w < count; ++w) { float weight = weights[offset++].weight; if (float.IsNaN(weight) || float.IsInfinity(weight)) nonfinite++; else sum += weight; }
                    sumError = Mathf.Max(sumError, Mathf.Abs(sum - 1));
                }
                counts.Dispose(); weights.Dispose();
                rendererReports.Add(new RendererReport { path = Relative(renderer.transform, prefab.transform), mesh = mesh.name, vertices = mesh.vertexCount, triangles = triangles, submeshes = mesh.subMeshCount, bones = boneNames, shapes = shapes.ToArray(), boundsCenter = mesh.bounds.center, boundsSize = mesh.bounds.size, unweightedVertices = unweighted, nonfiniteWeights = nonfinite, maxWeightSumError = sumError, maxInfluences = maximumInfluences, verticesAboveFourWeights = aboveFour, materials = renderer.sharedMaterials.Select(m => m == null ? "<NULL>" : m.name + ":" + m.shader.name).ToArray() });
                triangleTotal += triangles; vertexTotal += mesh.vertexCount; materialTotal += renderer.sharedMaterials.Length; shapeTotal += mesh.blendShapeCount;
            }
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh; if (mesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
                int triangles = 0; for (int s = 0; s < mesh.subMeshCount; ++s) triangles += (int)mesh.GetIndexCount(s) / 3;
                triangleTotal += triangles; vertexTotal += mesh.vertexCount; materialTotal += filter.GetComponent<MeshRenderer>().sharedMaterials.Length;
                rendererReports.Add(new RendererReport { path = Relative(filter.transform, prefab.transform), mesh = mesh.name, vertices = mesh.vertexCount, triangles = triangles, submeshes = mesh.subMeshCount, bones = Array.Empty<string>(), shapes = Array.Empty<ShapeReport>(), boundsCenter = mesh.bounds.center, boundsSize = mesh.bounds.size });
            }
            var clips = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).Select(c => new ClipReport { name = c.name, length = c.length, frameRate = c.frameRate, humanMotion = c.humanMotion, blendShapeCurves = AnimationUtility.GetCurveBindings(c).Where(b => b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)).Select(b => b.path + ":" + b.propertyName).ToArray() }).ToArray();
            var report = new ImportReport { asset = asset, source = source, sha256 = source == null ? null : Hash(source), unityVersion = Application.unityVersion, animationType = importer.animationType.ToString(), avatarValid = animator != null && animator.avatar != null && animator.avatar.isValid, avatarHuman = animator != null && animator.avatar != null && animator.avatar.isHuman, importBlendShapes = importer.importBlendShapes, optimizeGameObjects = importer.optimizeGameObjects, animationCompression = importer.animationCompression.ToString(), meshCompression = importer.meshCompression.ToString(), globalScale = importer.globalScale, skinWeightsMode = importer.skinWeights.ToString(), maximumImportedInfluences = importer.maxBonesPerVertex, minimumImportedWeight = importer.minBoneWeight, weldVertices = importer.weldVertices, optimizeMeshVertices = importer.optimizeMeshVertices, projectSkinWeights = QualitySettings.skinWeights.ToString(), renderedTriangles = triangleTotal, vertices = vertexTotal, rendererCount = rendererReports.Count, materialSlots = materialTotal, uniqueDeformBones = allBones.Count, blendShapeCount = shapeTotal, bones = allBones.OrderBy(b => b).ToArray(), transforms = prefab.GetComponentsInChildren<Transform>(true).Select(t => Relative(t, prefab.transform)).ToArray(), renderers = rendererReports.ToArray(), clips = clips };
            return SaveJson(Path.GetFileNameWithoutExtension(asset) + "_import_audit.json", report);
        }

        static void FaceNumericTest(string asset, string[] channels, string name)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(asset));
            instance.hideFlags = HideFlags.HideAndDontSave;
            var animator = instance.GetComponent<Animator>(); if (animator != null) animator.enabled = false;
            var samples = new List<FaceSample>(); float neutralError = 0;
            try
            {
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    for (int shape = 0; shape < renderer.sharedMesh.blendShapeCount; ++shape) renderer.SetBlendShapeWeight(shape, 0);
                    var baked = new Mesh(); renderer.BakeMesh(baked); var neutral = baked.vertices;
                    foreach (string channel in channels)
                    {
                        int shape = renderer.sharedMesh.GetBlendShapeIndex(channel); if (shape < 0) continue;
                        foreach (float value in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                        {
                            renderer.SetBlendShapeWeight(shape, value * 100); renderer.BakeMesh(baked); var vertices = baked.vertices; float maximum = 0; bool finite = true;
                            for (int i = 0; i < vertices.Length; ++i) { finite &= Finite(vertices[i]); maximum = Mathf.Max(maximum, Vector3.Distance(vertices[i], neutral[i])); }
                            samples.Add(new FaceSample { renderer = renderer.name, channel = channel, normalizedInput = value, unityWeight = renderer.GetBlendShapeWeight(shape), finite = finite, maximumDeltaFromNeutral = maximum });
                        }
                        renderer.SetBlendShapeWeight(shape, 0); renderer.BakeMesh(baked); var restored = baked.vertices;
                        for (int i = 0; i < restored.Length; ++i) neutralError = Mathf.Max(neutralError, Vector3.Distance(restored[i], neutral[i]));
                    }
                    Object.DestroyImmediate(baked);
                }
            }
            finally { Object.DestroyImmediate(instance); }
            string status = samples.Count == 0 ? "FAIL_NO_CHANNELS" : samples.All(s => s.finite && Mathf.Abs(s.unityWeight - s.normalizedInput * 100) < 0.001f) && neutralError < 0.000001f ? "TECHNICAL_PASS" : "FAIL";
            SaveJson(name + "_face_numeric.json", new FaceTestReport { status = status, asset = asset, samples = samples.ToArray(), neutralMaximumDelta = neutralError });
        }

        static Texture2D CopyTexture(string source, string name, bool color, bool normal = false)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (!File.Exists(source)) throw new FileNotFoundException("Texture source missing", source);
            string path = AssetRoot + "/Textures/" + SafeName(name) + Path.GetExtension(source);
            File.Copy(source, Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)), true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; importer.sRGBTexture = color; importer.isReadable = !normal; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 4096; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void ApplyMaterials(string asset, string name, TextureSource[] textures)
        {
            if (textures.Length == 0) return;
            var importer = (ModelImporter)AssetImporter.GetAtPath(asset);
            for (int i = 0; i < textures.Length; ++i)
            {
                var source = textures[i]; string prefix = name + "_M" + i;
                var color = CopyTexture(source.baseColor, prefix + "_BaseColor", true);
                var roughness = CopyTexture(source.roughness, prefix + "_Roughness", false);
                var metallic = CopyTexture(source.metallic, prefix + "_Metallic", false);
                var normal = CopyTexture(source.normal, prefix + "_Normal", false, true);
                string materialPath = AssetRoot + "/Materials/" + prefix + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, materialPath); }
                material.SetColor("_BaseColor", Color.white); material.SetTexture("_BaseMap", color); material.SetFloat("_Metallic", 0); material.SetFloat("_Smoothness", 0.3f);
                if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
                if (roughness != null || metallic != null)
                {
                    int width = roughness != null ? roughness.width : metallic.width, height = roughness != null ? roughness.height : metallic.height;
                    var packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true); var pixels = new Color[width * height];
                    for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) { float u = (x + 0.5f) / width, v = (y + 0.5f) / height; float r = roughness == null ? 0.7f : roughness.GetPixelBilinear(u, v).r; float m = metallic == null ? 0 : metallic.GetPixelBilinear(u, v).r; pixels[y * width + x] = new Color(m, 0, 0, 1 - Mathf.Clamp01(r)); }
                    packed.SetPixels(pixels); packed.Apply(); string packedPath = AssetRoot + "/Textures/" + prefix + "_MetallicSmoothness.png"; File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", packedPath)), packed.EncodeToPNG()); Object.DestroyImmediate(packed); AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport);
                    var packedImporter = (TextureImporter)AssetImporter.GetAtPath(packedPath); packedImporter.sRGBTexture = false; packedImporter.textureCompression = TextureImporterCompression.Uncompressed; packedImporter.SaveAndReimport();
                    material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath)); material.SetFloat("_Smoothness", 1); material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source.material), material); EditorUtility.SetDirty(material);
            }
            importer.SaveAndReimport();
        }

        public static string BuildReview(string request)
        {
            var data = JsonUtility.FromJson<ImportRequest>(request); Build(AssetRoot + "/Models/" + SafeName(data.name) + ".fbx", data); AssetDatabase.SaveAssets(); return File.ReadAllText(Path.Combine(OutputRoot, data.name + "_build.json"));
        }
        static void Build(string asset, ImportRequest data)
        {
            string name = SafeName(data.name);
            string profilePath = AssetRoot + "/Profiles/" + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<LabProfileSO>(profilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<LabProfileSO>(); AssetDatabase.CreateAsset(profile, profilePath); }
            profile.faceChannels = data.faceChannels;
            if (profile.faceChannels.SequenceEqual(new[] { "BlinkLeft", "BlinkRight" })) profile.presets = new[] {
                new ExpressionPreset { name = "GentleSquint", values = new[] { .35f, .35f }, verification = "PARTIAL_BLINK_CHANNELS_ONLY" },
                new ExpressionPreset { name = "FocusedSquint", values = new[] { .65f, .65f }, verification = "PARTIAL_BLINK_CHANNELS_ONLY" },
                new ExpressionPreset { name = "EyesClosed", values = new[] { 1f, 1f }, verification = "PARTIAL_BLINK_CHANNELS_ONLY" } };
            EditorUtility.SetDirty(profile);
            string controllerPath = AssetRoot + "/Controllers/" + name + ".controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existing != null) AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var available = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            var states = new List<string>(); var lengths = new List<float>(); var missing = new List<string>(); var stripped = new List<string>();
            foreach (var clipSource in data.clips)
            {
                var clip = available.FirstOrDefault(c => c.name == clipSource.take);
                if (clip == null) { missing.Add(clipSource.state + "=" + clipSource.take); continue; }
                var copy = Object.Instantiate(clip); copy.name = name + "_" + clipSource.state;
                foreach (var binding in AnimationUtility.GetCurveBindings(copy))
                    if (binding.type == typeof(SkinnedMeshRenderer) && data.faceChannels.Any(channel => binding.propertyName == "blendShape." + channel)) { AnimationUtility.SetEditorCurve(copy, binding, null); stripped.Add(clip.name + ":" + binding.path + ":" + binding.propertyName); }
                var settings = AnimationUtility.GetAnimationClipSettings(copy); settings.loopTime = clipSource.state == "Idle" || clipSource.state == "Walk" || clipSource.state == "Run"; settings.loopBlend = false; AnimationUtility.SetAnimationClipSettings(copy, settings);
                string path = AssetRoot + "/Animations/" + copy.name + ".anim"; if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(copy, path);
                var state = controller.layers[0].stateMachine.AddState(clipSource.state); state.motion = copy; state.writeDefaultValues = false; if (states.Count == 0) controller.layers[0].stateMachine.defaultState = state; states.Add(clipSource.state); lengths.Add(copy.length);
            }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject("C02 Rig Face Lab — Independent");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(asset), scene); model.transform.SetParent(root.transform, false);
                var animator = model.GetComponent<Animator>(); if (animator == null) animator = model.AddComponent<Animator>(); animator.runtimeAnimatorController = states.Count > 0 ? controller : null; animator.enabled = states.Count > 0; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (states.Count > 0) { animator.Rebind(); animator.Play(states[0], 0, 0); animator.Update(0); }
                var face = root.AddComponent<LabFaceController>(); face.profile = profile; face.model = model.transform; face.Rebind();
                var skinning = root.AddComponent<LabSkinningScope>(); skinning.allowMoreThanFourWeights = data.maximumInfluences > 4;
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.quality = SkinQuality.Auto;
                var playback = root.AddComponent<LabPlaybackController>(); playback.profile = profile; playback.bodyAnimator = animator; playback.states = states.ToArray(); playback.clipSeconds = lengths.ToArray(); playback.outputDirectory = Path.Combine(OutputRoot, name + "_Runtime");
                var ui = root.AddComponent<LabReviewUI>(); ui.face = face; ui.playback = playback;
                Bounds bounds = ActualSkinBounds(model);
                float height = bounds.size.y; if (height < 0.1f || height > 10) throw new InvalidOperationException("Unexpected imported scale; do not compensate silently: " + height);
                var head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null; Vector3 faceTarget = head == null ? new Vector3(bounds.center.x, bounds.max.y - height * 0.1f, bounds.center.z) : head.position + Vector3.up * height * 0.025f;
                var full = MakeCamera("Full body review", new Vector3(bounds.center.x, bounds.center.y, bounds.center.z + height * 2.1f), bounds.center, 38);
                var near = MakeCamera("Face close review", faceTarget + new Vector3(0.14f, 0.02f, 0.67f), faceTarget, 35);
                var game = MakeCamera("Game distance reference — not gameplay", profile.gameReferenceOffset + new Vector3(bounds.center.x, animator.transform.position.y, bounds.center.z), Vector3.zero, profile.gameReferenceFov); game.transform.rotation = Quaternion.identity;
                ui.cameras = new[] { full, near, game }; foreach (var reviewCamera in ui.cameras) reviewCamera.transform.SetParent(root.transform, true); ui.SelectCamera(0);
                var capture = root.AddComponent<LabCapture>(); capture.profile = profile; capture.face = face; capture.playback = playback; capture.captureCamera = near;
                var light = new GameObject("Lab key light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.7f; light.transform.rotation = Quaternion.Euler(35, -30, 0); light.shadows = LightShadows.Soft;
                var fill = new GameObject("Lab fill light").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.6f; fill.transform.rotation = Quaternion.Euler(10, 140, 0);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f); RenderSettings.fog = false;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Inspection ground"; ground.transform.position = new Vector3(0, animator.transform.position.y, 0); ground.transform.localScale = Vector3.one * 0.5f; Object.DestroyImmediate(ground.GetComponent<Collider>());
                var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); groundMat.SetColor("_BaseColor", new Color(0.19f, 0.21f, 0.23f)); groundMat.SetFloat("_Smoothness", 0); string groundPath = AssetRoot + "/Materials/InspectionGround.mat"; var groundAsset = AssetDatabase.LoadAssetAtPath<Material>(groundPath); if (groundAsset == null) { AssetDatabase.CreateAsset(groundMat, groundPath); groundAsset = groundMat; } else Object.DestroyImmediate(groundMat); ground.GetComponent<Renderer>().sharedMaterial = groundAsset;
                string prefabPath = AssetRoot + "/Prefabs/" + name + "_LabRig.prefab"; PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                string scenePath = AssetRoot + "/Scenes/" + name + "_Review.unity"; EditorSceneManager.SaveScene(scene, scenePath);
                SaveJson(name + "_build.json", new BuildReport { scene = scenePath, prefab = prefabPath, controller = controllerPath, profile = profilePath, states = states.ToArray(), missingRequestedClips = missing.ToArray(), removedFacialCurvesFromExperimentCopies = stripped.ToArray(), cameraReference = profile.gameReferenceSource });
            }
            finally { EditorSceneManager.CloseScene(scene, true); if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }
        static Camera MakeCamera(string name, Vector3 position, Vector3 target, float fov)
        {
            var camera = new GameObject(name).AddComponent<Camera>(); camera.transform.position = position; camera.transform.LookAt(target); camera.fieldOfView = fov; camera.nearClipPlane = 0.03f; camera.farClipPlane = 50; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.22f, 0.24f, 0.27f); return camera;
        }
        static Bounds ActualSkinBounds(GameObject model)
        {
            bool hasPoint = false; Bounds bounds = new Bounds(); var baked = new Mesh();
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.BakeMesh(baked);
                foreach (var vertex in baked.vertices) { Vector3 point = renderer.transform.TransformPoint(vertex); if (!hasPoint) { bounds = new Bounds(point, Vector3.zero); hasPoint = true; } else bounds.Encapsulate(point); }
            }
            Object.DestroyImmediate(baked);
            if (!hasPoint) throw new InvalidOperationException("No evaluated skinned vertices for inspection framing");
            return bounds;
        }
        [Serializable] class CalibrationReport { public string status; public string scope; public Vector3 actualIdleMinimum; public Vector3 actualIdleMaximum; public float originalGroundY; public float calibratedGroundY; public Vector3 fullCameraPosition; public Vector3 faceCameraPosition; }
        public static string CalibrateReview()
        {
            if (EditorApplication.isPlaying || !SceneManager.GetActiveScene().path.StartsWith(AssetRoot + "/Scenes/", StringComparison.Ordinal)) throw new InvalidOperationException("Open saved experiment scene in Edit Mode");
            var ui = Object.FindFirstObjectByType<LabReviewUI>(); var playback = ui.playback; var animator = playback.bodyAnimator;
            animator.enabled = playback.states.Length > 0; if (animator.enabled) { animator.Rebind(); animator.Play("Idle", 0, 0); animator.Update(0); }
            ui.face.Neutral(); var bounds = ActualSkinBounds(animator.gameObject); var ground = GameObject.Find("Inspection ground"); float before = ground.transform.position.y;
            ground.transform.position = new Vector3(0, animator.transform.position.y, 0);
            ui.cameras[0].transform.position = bounds.center + new Vector3(0, 0, bounds.size.y * 2.1f); ui.cameras[0].transform.LookAt(bounds.center);
            var head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null; var face = head == null ? new Vector3(bounds.center.x, bounds.max.y - 0.13f, bounds.center.z) : head.position + Vector3.up * bounds.size.y * 0.025f;
            ui.cameras[1].transform.position = face + new Vector3(0.14f, 0.02f, 0.67f); ui.cameras[1].transform.LookAt(face);
            ui.cameras[2].transform.position = playback.profile.gameReferenceOffset + new Vector3(bounds.center.x, animator.transform.position.y, bounds.center.z); ui.cameras[2].transform.rotation = Quaternion.identity;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return SaveJson("final_camera_calibration.json", new CalibrationReport { status = "MEASURED", scope = "Ground is fixed at the unchanged character root Y (Blender source ground Z=0); it does not track deformed bounds. Fixed cameras use evaluated Idle frame0 geometry rather than conservative all-animation bounds. Character root and curves unchanged; pre-existing below-ground vertices are not hidden.", actualIdleMinimum = bounds.min, actualIdleMaximum = bounds.max, originalGroundY = before, calibratedGroundY = ground.transform.position.y, fullCameraPosition = ui.cameras[0].transform.position, faceCameraPosition = ui.cameras[1].transform.position });
        }
        [Serializable] class SkinQualitySample { public string state; public float normalizedTime; public int comparedVertices; public int verticesWithDifferentPositions; public float maximumDeltaFourVsUnlimited; public bool finite; }
        [Serializable] class ActualSkinQualityReport { public string status; public string scope; public string qualityBefore; public string qualityAfter; public SkinQualitySample[] samples; }
        public static string TestCandidateSkinQuality()
        {
            if (EditorApplication.isPlaying || !SceneManager.GetActiveScene().path.StartsWith(AssetRoot + "/Scenes/", StringComparison.Ordinal)) throw new InvalidOperationException("Experiment Edit Mode required");
            var playback = Object.FindFirstObjectByType<LabPlaybackController>(); var animator = playback.bodyAnimator; SkinWeights original = QualitySettings.skinWeights; var samples = new List<SkinQualitySample>(); var baked = new Mesh();
            try
            {
                foreach (string state in playback.states) foreach (float time in new[] { 0f, 0.25f, 0.5f, 0.75f })
                {
                    animator.Play(state, 0, time); animator.Update(0); var sample = new SkinQualitySample { state = state, normalizedTime = time, finite = true };
                    foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        renderer.quality = SkinQuality.Auto; QualitySettings.skinWeights = SkinWeights.Unlimited; renderer.BakeMesh(baked); var full = baked.vertices; QualitySettings.skinWeights = SkinWeights.FourBones; renderer.BakeMesh(baked); var limited = baked.vertices; sample.comparedVertices += full.Length;
                        for (int i = 0; i < full.Length; ++i) { float delta = Vector3.Distance(full[i], limited[i]); sample.finite &= Finite(full[i]) && Finite(limited[i]); if (delta > 0.000001f) sample.verticesWithDifferentPositions++; sample.maximumDeltaFourVsUnlimited = Mathf.Max(sample.maximumDeltaFourVsUnlimited, delta); }
                    }
                    samples.Add(sample);
                }
            }
            finally { QualitySettings.skinWeights = original; Object.DestroyImmediate(baked); animator.Play("Idle", 0, 0); animator.Update(0); }
            return SaveJson("actual_skin_quality_comparison.json", new ActualSkinQualityReport { status = samples.All(s => s.finite) ? "TECHNICAL_PASS" : "FAIL", scope = "Actual imported model CPU BakeMesh comparison with FourBones versus Unlimited at 16 body-clip samples. This verifies skin-quality effect, not loss against the pre-FBX 722-vertex source or artistic deformation.", qualityBefore = original.ToString(), qualityAfter = QualitySettings.skinWeights.ToString(), samples = samples.ToArray() });
        }
        public static string OpenReview(string request)
        {
            if (!request.StartsWith(AssetRoot + "/Scenes/", StringComparison.Ordinal) || !request.EndsWith(".unity", StringComparison.Ordinal)) throw new ArgumentException("Experiment scene only");
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Already playing");
            foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)) if (scene.isDirty) throw new InvalidOperationException("Unsaved scene present; refusing to replace open scenes: " + scene.path);
            EditorSceneManager.OpenScene(request, OpenSceneMode.Single); return Probe();
        }
        [Serializable] public class CaptureRequest { public string folder; public string mode = "face_sweep"; public int frames = 180; public int camera = 1; public int holdFrames = 15; public bool stopPlayWhenComplete; }
        public static string StartCapture(string request)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first");
            var data = JsonUtility.FromJson<CaptureRequest>(request);
            var capture = Object.FindFirstObjectByType<LabCapture>(); var ui = Object.FindFirstObjectByType<LabReviewUI>();
            if (capture == null || ui == null || !SceneManager.GetActiveScene().path.StartsWith(AssetRoot + "/Scenes/", StringComparison.Ordinal)) throw new InvalidOperationException("Experiment scene not active");
            capture.mode = data.mode; capture.requestedFrames = data.frames; capture.holdFrames = data.holdFrames; capture.stopPlayWhenComplete = data.stopPlayWhenComplete;
            capture.outputDirectory = Path.Combine(OutputRoot, SafeName(data.folder)); capture.captureCamera = ui.cameras[data.camera]; ui.SelectCamera(data.camera); capture.Begin();
            return "RUNNING: " + capture.outputDirectory;
        }
        [Serializable] class FiveWeightReport { public string status; public string scope = "Transient synthetic mesh: fifth influence retained by SetBoneWeights and evaluated by CPU BakeMesh. Candidate import, GPU visual output and performance require separate checks."; public string before; public string during; public string after; public int storedInfluences; public float expectedDelta; public float measuredDelta; }
        public static string TestFiveWeights()
        {
            var previousScene = SceneManager.GetActiveScene(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            SkinWeights previous = QualitySettings.skinWeights; var report = new FiveWeightReport { before = previous.ToString(), expectedDelta = 0.08f };
            var mesh = new Mesh(); var baked = new Mesh();
            try
            {
                var root = new GameObject("Transient five-weight diagnostic"); var renderer = root.AddComponent<SkinnedMeshRenderer>(); var bones = new Transform[5];
                for (int b = 0; b < bones.Length; ++b) { bones[b] = new GameObject("DiagnosticBone" + b).transform; bones[b].SetParent(root.transform, false); }
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 }; mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
                var counts = new Unity.Collections.NativeArray<byte>(new byte[] { 5, 5, 5 }, Unity.Collections.Allocator.Temp);
                var weights = new Unity.Collections.NativeArray<BoneWeight1>(15, Unity.Collections.Allocator.Temp); float[] values = { 0.4f, 0.25f, 0.15f, 0.12f, 0.08f };
                for (int v = 0; v < 3; ++v) for (int b = 0; b < 5; ++b) weights[v * 5 + b] = new BoneWeight1 { boneIndex = b, weight = values[b] };
                mesh.SetBoneWeights(counts, weights); counts.Dispose(); weights.Dispose(); renderer.sharedMesh = mesh; renderer.bones = bones; renderer.rootBone = root.transform; renderer.quality = SkinQuality.Auto;
                var stored = mesh.GetBonesPerVertex(); report.storedInfluences = stored[0]; stored.Dispose();
                QualitySettings.skinWeights = SkinWeights.Unlimited; report.during = QualitySettings.skinWeights.ToString(); renderer.BakeMesh(baked); var neutral = baked.vertices; bones[4].localPosition = Vector3.forward; renderer.BakeMesh(baked); report.measuredDelta = (baked.vertices[0] - neutral[0]).z;
                report.status = report.storedInfluences == 5 && Mathf.Abs(report.measuredDelta - report.expectedDelta) < 0.00001f ? "TECHNICAL_PASS" : "FAIL";
            }
            finally { QualitySettings.skinWeights = previous; report.after = QualitySettings.skinWeights.ToString(); Object.DestroyImmediate(mesh); Object.DestroyImmediate(baked); EditorSceneManager.CloseScene(scene, true); if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene); }
            return SaveJson("five_weight_cpu_fixture.json", report);
        }
    }
}
